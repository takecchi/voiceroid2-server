using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
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
    public void Talk(string text, string? speaker)
    {
        var payload = ApplySpeakerPrefix(text, speaker);

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
    public void SaveAudio(string text, string? speaker, string outPath)
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

        _stopBtn.EmulateClick();
        _textBox.EmulateChangeText(payload);

        // 音声保存ボタンを押すと SaveFileDialog がモーダルで出る。
        // EmulateClick はモーダルが閉じるまで戻らないため、別スレッドで
        // ダイアログを掴んで自動操作する必要がある。
        var dialogPump = new Thread(() => new SaveAudioFlow(_mainWindow.Handle, absPath).Run())
        {
            IsBackground = true,
            Name = "save-dialog-pump",
        };
        dialogPump.Start();

        _saveBtn.EmulateClick();
        dialogPump.Join(TimeSpan.FromMinutes(2));

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
    /// 話者一覧を返す。VOICEROID2 自体は外部 API を持たないため確実な手段が無い。
    /// 実機で確認した上で、以下のいずれかの方式に置き換える想定:
    ///   (a) インストールディレクトリの voice/* 構成ファイルを読む
    ///   (b) Codeer.Friendly.Dynamic 経由でメインウィンドウの話者 ComboBox の
    ///       ItemsSource を投影する
    /// 現状は空配列を返すだけのスタブ。
    /// </summary>
    public IReadOnlyList<string> ListSpeakers()
    {
        LogWriter.Warn("ListSpeakers is not implemented; returning empty list");
        return Array.Empty<string>();
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
        // VOICEROID2 既定の話者切替記号は全角 '＞' (U+FF1E)
        return $"{speaker}＞{text}";
    }

    private static string Preview(string text)
    {
        const int max = 40;
        return text.Length <= max ? text : text.Substring(0, max) + "...";
    }
}
