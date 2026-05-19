using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
/// VOICEROID2 の「音声保存」操作で開く一連のダイアログを自動操作する。
///
/// 実機検証 (2026-05):
///   - 音声保存ボタンを押すと、まず VOICEROID2 自前の WPF 設定ダイアログ
///     (title="音声保存", class=HwndWrapper[...]) が出る (「音声保存時に毎回設定を表示する」が
///     ON のとき)。RadioButton (出力モード) + CheckBox (各種オプション) + OK / キャンセル。
///   - OK を押すと **実際のファイル保存ダイアログ** が開く (Win32 SaveFileDialog `#32770`
///     の可能性が高いが、WPF カスタムの可能性もあるので両対応する)。
///   - その後、「合成音声をファイルに保存しました。」等の通知モーダル → OK。
///   - 「音声保存時に毎回設定を表示する」が OFF のときは設定ダイアログが省かれて
///     いきなりファイル保存ダイアログから始まる。HWND ベースの状態機械なのでどちらも通る。
///
/// 同一 HWND を二度操作しないように処理済み HWND を Set で管理する。
/// ボタン検索は ButtonBase 全部ではなく System.Windows.Controls.Button に限定
/// (CheckBox / RadioButton の Content に "保存" "OK" を含む文字列があるため誤検出回避)。
/// </summary>
internal sealed class SaveAudioFlow
{
    private static readonly TimeSpan TotalDeadline = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(150);
    private const string RealButtonTypeFullName = "System.Windows.Controls.Button";

    private readonly WindowsAppFriend _app;
    private readonly WindowControl _mainWindow;
    private readonly string _outPath;
    private readonly int _threadId;

    public SaveAudioFlow(WindowsAppFriend app, WindowControl mainWindow, string outPath)
    {
        _app = app;
        _mainWindow = mainWindow;
        _outPath = outPath;
        _threadId = NativeWindows.GetWindowThreadProcessId(mainWindow.Handle, out _);
    }

    public void Run()
    {
        var sw = Stopwatch.StartNew();
        var processed = new HashSet<IntPtr>();

        while (sw.Elapsed < TotalDeadline)
        {
            Thread.Sleep(TickInterval);

            var dialog = FindUnhandledDialog(processed);
            if (dialog != null)
            {
                if (TryHandleDialog(dialog))
                {
                    processed.Add(dialog.Handle);
                }
                continue;
            }

            if (processed.Count > 0 && File.Exists(_outPath))
            {
                return;
            }
        }

        LogWriter.Warn(
            $"SaveAudioFlow.Run gave up: elapsed={sw.Elapsed.TotalSeconds:F1}s " +
            $"processed={processed.Count}");
    }

    private WindowControl? FindUnhandledDialog(HashSet<IntPtr> processed)
    {
        WindowControl? hit = null;
        NativeWindows.EnumThreadWindows(_threadId, (hWnd, _) =>
        {
            if (hWnd == _mainWindow.Handle) return true;
            if (processed.Contains(hWnd)) return true;
            if (!NativeWindows.IsWindowVisible(hWnd)) return true;
            var title = NativeWindows.ReadWindowText(hWnd);
            if (string.IsNullOrEmpty(title)) return true;
            hit = new WindowControl(_app, hWnd);
            return false;
        }, IntPtr.Zero);
        return hit;
    }

    private bool TryHandleDialog(WindowControl dialog)
    {
        var className = NativeWindows.ReadClassName(dialog.Handle);
        return className == "#32770"
            ? HandleWin32SaveDialog(dialog.Handle)
            : HandleWpfDialog(dialog);
    }

    // -------- WPF dialog handler --------

    private bool HandleWpfDialog(WindowControl dialog)
    {
        IWPFDependencyObjectCollection<DependencyObject> tree;
        try { tree = dialog.LogicalTree(); }
        catch (Exception ex)
        {
            LogWriter.Warn($"LogicalTree failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }

        // 設定ダイアログ: 出力モードの RadioButton が居る
        if (HasButtonContent(tree, "１つのファイルに書き出す"))
        {
            return ClickRealButtonByContent(tree, "OK");
        }

        // WPF 製ファイル保存ダイアログ: 真の Button で Content に "保存" を含むものがある
        var saveBtn = FindRealButtonByContent(tree, "保存");
        if (saveBtn != null)
        {
            return TryFillWpfSaveDialog(tree, saveBtn);
        }

        // 通知 / 情報系: 真の Button で OK のみ
        if (FindRealButtonByContent(tree, "OK") != null)
        {
            return ClickRealButtonByContent(tree, "OK");
        }

        LogWriter.Warn(
            $"unknown WPF dialog (title=\"{NativeWindows.ReadWindowText(dialog.Handle)}\"); cannot handle");
        return false;
    }

    private bool TryFillWpfSaveDialog(
        IWPFDependencyObjectCollection<DependencyObject> tree, AppVar saveBtn)
    {
        var textBoxes = tree.ByType<TextBox>();
        if (textBoxes.Count == 0)
        {
            LogWriter.Warn("WPF save: no TextBox in dialog");
            return false;
        }

        AppVar target = textBoxes[0];
        for (var i = 0; i < textBoxes.Count; i++)
        {
            var t = TryReadString(textBoxes[i], "Text") ?? "";
            if (t.Contains("\\") || t.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                target = textBoxes[i];
                break;
            }
        }
        try
        {
            new WPFTextBox(target).EmulateChangeText(_outPath);
            new WPFButtonBase(saveBtn).EmulateClick(new Async());
            return true;
        }
        catch (Exception ex)
        {
            LogWriter.Warn($"WPF save: fill/click failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    // -------- Win32 (#32770) dialog handler --------

    private bool HandleWin32SaveDialog(IntPtr dialog)
    {
        var edit = LocateFileNameEditWin32(dialog);
        if (edit != IntPtr.Zero)
        {
            // ファイル名 Edit があれば SaveFileDialog 本体
            NativeWindows.SendMessage(edit, NativeWindows.WM_SETTEXT, IntPtr.Zero, _outPath);
            Thread.Sleep(60);
            foreach (var label in new[] { "保存(&S)", "保存", "Save", "&Save" })
            {
                if (ClickWin32ButtonByText(dialog, label))
                {
                    return true;
                }
            }
            LogWriter.Warn("Win32 save: 保存 button not found");
            return false;
        }

        // Edit が無ければ確認/通知ダイアログ (上書き確認 / 「保存しました」等)。
        // 上書き確認 (Win32 標準): "ファイル名は既に存在します。置換しますか？" + はい/いいえ
        // 通常パスでは Voiceroid2Driver.SaveAudio が事前 File.Delete するので出ないが
        // 出てきた場合の防衛策として はい/Yes を押す。
        foreach (var label in new[] { "はい(&Y)", "はい", "Yes", "&Yes", "OK" })
        {
            if (ClickWin32ButtonByText(dialog, label))
            {
                return true;
            }
        }
        LogWriter.Warn(
            $"unrecognized Win32 #32770 (title=\"{NativeWindows.ReadWindowText(dialog)}\"); " +
            "no Edit, no known confirm button");
        return false;
    }

    private static IntPtr LocateFileNameEditWin32(IntPtr dialog)
    {
        // 旧スタイル: ComboBoxEx32 > ComboBox > Edit
        var comboEx = NativeWindows.FindWindowEx(dialog, IntPtr.Zero, "ComboBoxEx32", null);
        if (comboEx != IntPtr.Zero)
        {
            var combo = NativeWindows.FindWindowEx(comboEx, IntPtr.Zero, "ComboBox", null);
            if (combo != IntPtr.Zero)
            {
                var edit = NativeWindows.FindWindowEx(combo, IntPtr.Zero, "Edit", null);
                if (edit != IntPtr.Zero) return edit;
            }
        }
        var directEdit = NativeWindows.FindWindowEx(dialog, IntPtr.Zero, "Edit", null);
        if (directEdit != IntPtr.Zero) return directEdit;
        return FindDescendantWin32(dialog, "Edit");
    }

    private static IntPtr FindDescendantWin32(IntPtr parent, string className)
    {
        IntPtr hit = IntPtr.Zero;
        NativeWindows.EnumChildWindows(parent, (hWnd, _) =>
        {
            if (NativeWindows.ReadClassName(hWnd) == className)
            {
                hit = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return hit;
    }

    private static bool ClickWin32ButtonByText(IntPtr parent, string labelFragment)
    {
        IntPtr hit = IntPtr.Zero;
        NativeWindows.EnumChildWindows(parent, (hWnd, _) =>
        {
            if (NativeWindows.ReadClassName(hWnd) == "Button"
                && NativeWindows.ReadWindowText(hWnd).Contains(labelFragment))
            {
                hit = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        if (hit == IntPtr.Zero) return false;
        NativeWindows.SendMessage(hit, NativeWindows.BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        return true;
    }

    // -------- WPF helpers --------

    private static bool HasButtonContent(
        IWPFDependencyObjectCollection<DependencyObject> tree, string fragment)
    {
        var buttons = tree.ByType<ButtonBase>();
        for (var i = 0; i < buttons.Count; i++)
        {
            var c = TryReadString(buttons[i], "Content");
            if (c != null && c.Contains(fragment)) return true;
        }
        return false;
    }

    /// <summary>
    /// System.Windows.Controls.Button (RadioButton / CheckBox / ToggleButton を除外) で
    /// Content に fragment を含むものを返す。
    /// </summary>
    private static AppVar? FindRealButtonByContent(
        IWPFDependencyObjectCollection<DependencyObject> tree, string fragment)
    {
        var buttons = tree.ByType<ButtonBase>();
        for (var i = 0; i < buttons.Count; i++)
        {
            var typeName = TryReadTypeFullName(buttons[i]);
            if (typeName != RealButtonTypeFullName) continue;
            var c = TryReadString(buttons[i], "Content");
            if (c != null && c.Contains(fragment)) return buttons[i];
        }
        return null;
    }

    private static bool ClickRealButtonByContent(
        IWPFDependencyObjectCollection<DependencyObject> tree, string fragment)
    {
        var btn = FindRealButtonByContent(tree, fragment);
        if (btn == null)
        {
            LogWriter.Warn($"real Button with content=\"{fragment}\" not found");
            return false;
        }
        try
        {
            // 押した直後に別モーダルが開く可能性があるので Async で発火
            new WPFButtonBase(btn).EmulateClick(new Async());
            return true;
        }
        catch (Exception ex)
        {
            LogWriter.Warn($"Button click failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static string? TryReadString(AppVar obj, string propertyName)
    {
        try
        {
            return obj[propertyName]().Core as string;
        }
        catch
        {
            return null;
        }
    }

    private static string TryReadTypeFullName(AppVar obj)
    {
        try
        {
            return (obj["GetType"]()["FullName"]().Core as string) ?? "";
        }
        catch
        {
            return "";
        }
    }
}
