using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Codeer.Friendly;
using Codeer.Friendly.Windows;
using Codeer.Friendly.Windows.Grasp;
using RM.Friendly.WPFStandardControls;

namespace Voiceroid2Helper;

/// <summary>
/// VOICEROID2 にアタッチし、編集ビュー上のコントロールを発見して
/// 高レベル操作 (Talk / SaveAudio / 話者列挙) を提供する。
/// </summary>
internal sealed class Voiceroid2Driver : IDisposable
{
    private static readonly TimeSpan AttachDeadline = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(75);

    // VOICEROID2 (AI.Talk.Editor) の WPF MVVM が公開しているコマンド名。
    // バージョンが変わって動かなくなった場合は Snoop / UIA Verify で確認して差し替える。
    private const string EditorViewTypeFullName = "AI.Talk.Editor.TextEditView";
    private const string PlayCommand = "PlayCommand";
    private const string StopCommand = "StopCommand";

    // 「先頭へ戻る」コマンドはバージョンで揺れる (実機で見つからない事例あり)。
    // 候補に全部当たらなければ null のままにして、その場合は再生完了待ちをスキップする。
    private static readonly string[] SeekToHeadCommandCandidates =
    {
        "MoveToBeginningCommand",
        "SeekToBeginningCommand",
        "HeadCommand",
    };

    private static readonly string[] SaveCommandCandidates =
    {
        "SaveWaveCommand",
        "SaveAudioCommand",
        "OutputWaveCommand",
        "SaveVoiceCommand",
    };

    private readonly Process _process;
    private readonly WindowsAppFriend _app;
    private readonly WindowControl _mainWindow;
    private readonly WPFTextBox _textBox;
    private readonly WPFButtonBase _playBtn;
    private readonly WPFButtonBase _stopBtn;
    private readonly WPFButtonBase? _seekHeadBtn;
    private readonly WPFButtonBase? _saveBtn;

    private Voiceroid2Driver(
        Process process,
        WindowsAppFriend app,
        WindowControl mainWindow,
        WPFTextBox textBox,
        WPFButtonBase playBtn,
        WPFButtonBase stopBtn,
        WPFButtonBase? seekHeadBtn,
        WPFButtonBase? saveBtn)
    {
        _process = process;
        _app = app;
        _mainWindow = mainWindow;
        _textBox = textBox;
        _playBtn = playBtn;
        _stopBtn = stopBtn;
        _seekHeadBtn = seekHeadBtn;
        _saveBtn = saveBtn;
    }

    public Process Process => _process;
    public WindowControl MainWindow => _mainWindow;

    /// <summary>
    /// VOICEROID2 にアタッチし、編集ビューの初期化完了を待ってから Driver を返す。
    /// </summary>
    public static Voiceroid2Driver AttachAndWaitReady()
    {
        var process = Voiceroid2Locator.AttachOrStart();
        var app = new WindowsAppFriend(process);

        var sw = Stopwatch.StartNew();
        Exception? lastError = null;

        while (sw.Elapsed < AttachDeadline)
        {
            try
            {
                var window = app.FromZTop();
                NativeWindows.ShowWindow(window.Handle, NativeWindows.SW_MINIMIZE);

                var editor = window
                    .GetFromTypeFullName(EditorViewTypeFullName)
                    .FirstOrDefault();
                if (editor == null)
                {
                    Thread.Sleep(PollInterval);
                    continue;
                }

                IWPFDependencyObjectCollection<DependencyObject> tree = editor.LogicalTree();
                AppVar textBox = tree.ByType<TextBox>().FirstOrDefault()
                    ?? throw new InvalidOperationException("text input control not found");
                AppVar play = RequireBinding(tree, PlayCommand);
                AppVar stop = RequireBinding(tree, StopCommand);
                AppVar? seekHead = FindOptionalBinding(tree, SeekToHeadCommandCandidates);
                AppVar? save = FindOptionalBinding(tree, SaveCommandCandidates);

                LogWriter.Info("editor ready");
                return new Voiceroid2Driver(
                    process,
                    app,
                    window,
                    new WPFTextBox(textBox),
                    new WPFButtonBase(play),
                    new WPFButtonBase(stop),
                    seekHead == null ? null : new WPFButtonBase(seekHead),
                    save == null ? null : new WPFButtonBase(save));
            }
            catch (Exception ex)
            {
                lastError = ex;
                Thread.Sleep(PollInterval);
            }
        }

        throw new TimeoutException(
            $"VOICEROID2 editor did not become ready within {AttachDeadline.TotalSeconds}s. " +
            $"last: {lastError?.GetType().Name}: {lastError?.Message}");
    }

    public void Dispose()
    {
        try { _app.Dispose(); }
        catch (Exception ex) { LogWriter.Warn($"friend dispose: {ex.Message}"); }
        // VOICEROID2 本体は次回呼び出しの起動コストを避けるため終了させない。
    }

    /// <summary>
    /// テキストを設定して再生ボタンを押す (スピーカー出力のみ)。
    /// 発話完了まで待ってからリターンする。
    /// </summary>
    public void Talk(string text, string? speaker, VoiceTuning tuning)
    {
        var payload = ApplySpeakerPrefix(text, speaker);

        ApplyTuning(tuning);
        _stopBtn.EmulateClick();
        _textBox.EmulateChangeText(payload);
        _seekHeadBtn?.EmulateClick();
        _playBtn.EmulateClick();

        WaitWhilePlaying();
        LogWriter.Info($"talked: {Preview(payload)}");
    }

    /// <summary>
    /// テキストを設定して「音声保存」を実行し WAV を outPath に書き出す。
    /// </summary>
    public void SaveAudio(string text, string? speaker, string outPath, VoiceTuning tuning)
    {
        if (_saveBtn == null)
        {
            throw new InvalidOperationException(
                "save command binding not found in current VOICEROID2 build. " +
                $"tried: {string.Join(", ", SaveCommandCandidates)}");
        }

        var payload = ApplySpeakerPrefix(text, speaker);
        var absPath = Path.GetFullPath(outPath);
        Directory.CreateDirectory(Path.GetDirectoryName(absPath)!);
        if (File.Exists(absPath))
        {
            File.Delete(absPath);
        }

        ApplyTuning(tuning);
        _stopBtn.EmulateClick();
        _textBox.EmulateChangeText(payload);

        // 音声保存ボタンを押すと VOICEROID2 が独自の WPF モーダル
        // (title="音声保存", class=HwndWrapper[...]) を ShowDialog する。
        // 同期 EmulateClick だとターゲット UI スレッドがモーダル内に閉じ込められ
        // 返らないため Async() で発火だけして、ダイアログ自動操作は
        // SaveAudioFlow に任せる (helper メインスレッド上で動かす)。
        _saveBtn.EmulateClick(new Async());
        new SaveAudioFlow(_app, _mainWindow, absPath).Run();

        // WAV ファイル生成を最大 30 秒待つ (保存処理に時間がかかることがある)
        var waitSw = Stopwatch.StartNew();
        while (!File.Exists(absPath) && waitSw.Elapsed < TimeSpan.FromSeconds(30))
        {
            Thread.Sleep(100);
        }
        if (!File.Exists(absPath))
        {
            throw new IOException($"WAV was not generated at {absPath}");
        }
        LogWriter.Info($"saved: {absPath}");
    }

    /// <summary>
    /// VOICEROID2 メインウィンドウ左ペインのボイスプリセット一覧 (ListView) から
    /// プリセット名 (= --speaker / "名前&gt;テキスト" の名前) を列挙する。
    ///
    /// 実機検証 (2026-05): ListView の Items は <c>AI.Talk.VoicePreset</c> 型で
    /// <c>PresetName</c> ("ついなちゃん（標準語）" 等) と <c>VoiceName</c> ("tsuina_44") を持つ。
    /// 該当 ListView は <c>Selector</c> 派生コントロールを走査して
    /// Items[0] が <c>PresetName</c> プロパティを持つかで判別している
    /// (バージョン揺れに備えて型名そのものは見ない)。
    ///
    /// VoicePreset 型は AI.Talk.dll の内部型でこちらでは参照できないため、
    /// Codeer.Friendly の AppVar 経由でプロパティを読む。<c>dynamic</c> は使わず
    /// 文字列キー indexer + Core で typed に取り出す (`.claude/rules/type-safety.md` 参照)。
    /// </summary>
    public IReadOnlyList<string> ListSpeakers()
    {
        var tree = _mainWindow.LogicalTree();
        var selectors = tree.ByType<Selector>();

        for (var idx = 0; idx < selectors.Count; idx++)
        {
            var names = TryReadPresetNames(selectors[idx]);
            if (names.Count > 0)
            {
                return names;
            }
        }

        LogWriter.Warn(
            "voice preset selector not found (no Selector has items with `PresetName`)");
        return Array.Empty<string>();
    }

    private static List<string> TryReadPresetNames(AppVar selector)
    {
        AppVar items;
        int count;
        try
        {
            items = selector["Items"]();
            count = (int)items["Count"]().Core;
        }
        catch
        {
            return new List<string>();
        }
        if (count == 0)
        {
            return new List<string>();
        }

        var result = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            string? name;
            try
            {
                var item = items["get_Item"](i);
                name = item["PresetName"]().Core as string;
            }
            catch
            {
                // PresetName を持たないアイテムが混ざる = この Selector はボイスプリセット用ではない
                return new List<string>();
            }
            if (!string.IsNullOrEmpty(name))
            {
                result.Add(name!);
            }
        }
        return result;
    }

    /// <summary>
    /// マスター効果パラメータ (音量・話速・高さ・抑揚) を 4 つとも設定する。
    /// 1.0 が VOICEROID2 既定値。
    ///
    /// 実機検証 (2026-05): VOICEROID2 のマスター効果 UI は
    /// <c>AI.Talk.Editor.MasterControlView</c> の中に <c>AI.Framework.Wpf.Controls.LinearFader</c>
    /// が 7 個並んでいて、先頭 4 つが 音量/話速/高さ/抑揚、後ろ 3 つがポーズ系。
    ///
    /// 内部 TextBox.Text を書き換えても LinearFader の Value DP に伝播しない (TwoWay バインドが
    /// LostFocus トリガーで張られているっぽい) ため、LinearFader.set_Value を直接呼んで
    /// VM 側に値を反映させる。
    /// </summary>
    private void ApplyTuning(VoiceTuning tuning)
    {
        var masterView = _mainWindow
            .GetFromTypeFullName(MasterControlViewTypeFullName)
            .FirstOrDefault();
        if (masterView == null)
        {
            LogWriter.Warn($"tuning: {MasterControlViewTypeFullName} not found; skipped");
            return;
        }
        var faders = FindByTypeFullName(masterView.LogicalTree(), LinearFaderTypeFullName);
        if (faders.Length < 4)
        {
            LogWriter.Warn(
                $"tuning: MasterControlView has only {faders.Length} LinearFaders (need >=4); skipped");
            return;
        }

        SetFaderValue(faders[0], tuning.Volume, "volume");
        SetFaderValue(faders[1], tuning.Speed, "speed");
        SetFaderValue(faders[2], tuning.Pitch, "pitch");
        SetFaderValue(faders[3], tuning.Intonation, "intonation");
    }

    private const string MasterControlViewTypeFullName = "AI.Talk.Editor.MasterControlView";
    private const string LinearFaderTypeFullName = "AI.Framework.Wpf.Controls.LinearFader";

    /// <summary>
    /// LogicalTree 内で <paramref name="typeFullName"/> に完全一致する型の要素だけを拾う。
    /// LinearFader は本プロジェクトで型参照を持たないため <c>ByType&lt;T&gt;</c> が使えず、
    /// 文字列で型 FullName を比較する。
    /// </summary>
    private static AppVar[] FindByTypeFullName(
        IWPFDependencyObjectCollection<DependencyObject> tree, string typeFullName)
    {
        var result = new List<AppVar>();
        for (var i = 0; i < tree.Count; i++)
        {
            var item = tree[i];
            string fullName;
            try { fullName = (item["GetType"]()["FullName"]().Core as string) ?? ""; }
            catch { continue; }
            if (fullName == typeFullName) result.Add(item);
        }
        return result.ToArray();
    }

    private static void SetFaderValue(AppVar fader, double value, string paramName)
    {
        try
        {
            // LinearFader.Value (DependencyProperty) の CLR setter を直接呼ぶ。
            // set_Value(double) は WPF が自動生成する setter の IL 名で、Codeer.Friendly が
            // リフレクションで解決する。プリミティブ (double) はそのまま marshal される。
            fader["set_Value"](value);
        }
        catch (Exception ex)
        {
            LogWriter.Warn(
                $"tuning: {paramName}={value:F2} via LinearFader.set_Value failed: " +
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void WaitWhilePlaying()
    {
        // 再生中は「先頭へ戻る」ボタンが Disabled になる挙動を流用して完了を検出する。
        // バインディングを取得できなかった場合は単純なスリープで諦める (上位レイヤの
        // キュー直列化に任せる)。
        Thread.Sleep(150);
        if (_seekHeadBtn == null)
        {
            return;
        }
        var sw = Stopwatch.StartNew();
        var maxWait = TimeSpan.FromMinutes(5);
        while (!_seekHeadBtn.IsEnabled && sw.Elapsed < maxWait)
        {
            Thread.Sleep(80);
        }
    }

    private static AppVar RequireBinding(
        IWPFDependencyObjectCollection<DependencyObject> tree, string bindingName)
    {
        return tree.ByBinding(bindingName).FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"binding `{bindingName}` not found in editor view");
    }

    private static AppVar? FindOptionalBinding(
        IWPFDependencyObjectCollection<DependencyObject> tree, IEnumerable<string> candidates)
    {
        foreach (var name in candidates)
        {
            var hit = tree.ByBinding(name).FirstOrDefault();
            if (hit != null)
            {
                LogWriter.Info($"resolved binding: {name}");
                return hit;
            }
        }
        return null;
    }

    private static string ApplySpeakerPrefix(string text, string? speaker)
    {
        if (string.IsNullOrWhiteSpace(speaker)) return text;
        // VOICEROID2 既定の話者切替記号は半角 '>' (ASCII 0x3E)。
        // README / 仕様書類で全角 '＞' (U+FF1E) と書かれていることがあるが
        // 実機検証で半角でないと話者切替が効かないことを確認済み (2026-05)。
        return $"{speaker}>{text}";
    }

    private static string Preview(string text)
    {
        const int max = 40;
        return text.Length <= max ? text : text.Substring(0, max) + "...";
    }
}
