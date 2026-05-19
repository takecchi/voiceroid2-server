using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Voiceroid2Helper;

/// <summary>
/// 「音声保存」操作で開く一連のモーダルを別スレッドから操作するヘルパ。
/// 想定フロー:
///   1. (任意) 「テキストが分割されました」等の注意ダイアログ → OK
///   2. SaveFileDialog (Win32 #32770)                     → ファイル名入力 → 保存
///   3. (任意) 「保存しました」完了ダイアログ                → OK
/// VOICEROID2 のバージョン / 設定で 1, 3 はスキップされることもある。
/// ラベルは日本語 UI 前提。
/// </summary>
internal sealed class SaveAudioFlow
{
    private static readonly TimeSpan TotalDeadline = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(120);

    private readonly int _threadId;
    private readonly IntPtr _mainWindow;
    private readonly string _outPath;

    public SaveAudioFlow(IntPtr mainWindow, string outPath)
    {
        _mainWindow = mainWindow;
        _outPath = outPath;
        _threadId = NativeWindows.GetWindowThreadProcessId(mainWindow, out _);
    }

    public void Run()
    {
        var sw = Stopwatch.StartNew();
        var noticeDismissed = false;
        var saveTriggered = false;

        while (sw.Elapsed < TotalDeadline)
        {
            Thread.Sleep(TickInterval);

            if (!saveTriggered)
            {
                var dialog = FindSaveFileDialog();
                if (dialog != IntPtr.Zero)
                {
                    if (FillFileNameAndConfirm(dialog, _outPath))
                    {
                        saveTriggered = true;
                        LogWriter.Info("save-file-dialog: confirmed");
                    }
                    continue;
                }
            }

            if (!noticeDismissed)
            {
                var notice = FindModalByTitleContains("VOICEROID");
                if (notice != IntPtr.Zero && notice != _mainWindow)
                {
                    if (ClickButtonByText(notice, "OK") || ClickButtonByText(notice, "はい"))
                    {
                        noticeDismissed = true;
                        LogWriter.Info("notice: dismissed");
                        continue;
                    }
                }
            }

            if (saveTriggered)
            {
                // 完了ダイアログ (出ない場合もある)
                var done = FindModalByTitleContains("情報");
                if (done != IntPtr.Zero)
                {
                    ClickButtonByText(done, "OK");
                    break;
                }
                if (File.Exists(_outPath))
                {
                    break;
                }
            }
        }
    }

    private IntPtr FindSaveFileDialog()
    {
        IntPtr hit = IntPtr.Zero;
        NativeWindows.EnumThreadWindows(_threadId, (hWnd, _) =>
        {
            if (!NativeWindows.IsWindowVisible(hWnd)) return true;
            if (NativeWindows.ReadClassName(hWnd) != "#32770") return true;

            var title = NativeWindows.ReadWindowText(hWnd);
            if (title.Contains("保存") || title.Contains("Save"))
            {
                hit = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return hit;
    }

    private IntPtr FindModalByTitleContains(string fragment)
    {
        IntPtr hit = IntPtr.Zero;
        NativeWindows.EnumThreadWindows(_threadId, (hWnd, _) =>
        {
            if (hWnd == _mainWindow) return true;
            if (!NativeWindows.IsWindowVisible(hWnd)) return true;

            var title = NativeWindows.ReadWindowText(hWnd);
            if (!string.IsNullOrEmpty(title) && title.Contains(fragment))
            {
                hit = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return hit;
    }

    private static bool FillFileNameAndConfirm(IntPtr dialog, string filePath)
    {
        var edit = LocateFileNameEdit(dialog);
        if (edit == IntPtr.Zero)
        {
            LogWriter.Warn("filename edit control not found");
            return false;
        }

        NativeWindows.SendMessage(edit, NativeWindows.WM_SETTEXT, IntPtr.Zero, filePath);
        Thread.Sleep(60);

        // 「保存」「Save」「保存(&S)」あたりを順に試す
        foreach (var label in new[] { "保存(&S)", "保存", "Save", "&Save" })
        {
            if (ClickButtonByText(dialog, label))
            {
                return true;
            }
        }
        LogWriter.Warn("save button not found in dialog");
        return false;
    }

    private static IntPtr LocateFileNameEdit(IntPtr dialog)
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

        // Vista 以降の SaveFileDialog: 直下に Edit があることがある
        return NativeWindows.FindWindowEx(dialog, IntPtr.Zero, "Edit", null);
    }

    private static bool ClickButtonByText(IntPtr parent, string label)
    {
        var btn = FindButtonRecursive(parent, label);
        if (btn == IntPtr.Zero) return false;
        NativeWindows.SendMessage(btn, NativeWindows.BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        return true;
    }

    private static IntPtr FindButtonRecursive(IntPtr parent, string label)
    {
        IntPtr hit = IntPtr.Zero;
        NativeWindows.EnumChildWindows(parent, (hWnd, _) =>
        {
            if (NativeWindows.ReadClassName(hWnd) == "Button"
                && NativeWindows.ReadWindowText(hWnd).Contains(label))
            {
                hit = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return hit;
    }
}
