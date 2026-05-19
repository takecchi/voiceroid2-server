using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Voiceroid2Helper;

/// <summary>
/// VOICEROID2 (VoiceroidEditor.exe) の実行パス特定と起動を担当する。
/// 環境変数 → レジストリ複数キー → 既定インストールパスの順でフォールバックする。
/// </summary>
internal static class Voiceroid2Locator
{
    private const string ProcessName = "VoiceroidEditor";
    private const string ExeName = "VoiceroidEditor.exe";

    /// <summary>
    /// 既に起動済みのプロセスがあればそれを返し、無ければ最小化状態で新規起動する。
    /// </summary>
    public static Process AttachOrStart()
    {
        var running = Process.GetProcessesByName(ProcessName).FirstOrDefault();
        if (running != null)
        {
            LogWriter.Info($"attached to running pid={running.Id}");
            return running;
        }

        var exe = ResolveExePath()
            ?? throw new InvalidOperationException(
                "VOICEROID2 not installed (or could not be located). " +
                "Set VOICEROID2_PATH to VoiceroidEditor.exe explicitly.");

        LogWriter.Info($"starting {exe}");
        var p = Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            WindowStyle = ProcessWindowStyle.Minimized,
            UseShellExecute = true,
        }) ?? throw new InvalidOperationException($"failed to start: {exe}");

        return p;
    }

    private static string? ResolveExePath()
    {
        // 1. 環境変数で上書き
        var env = Environment.GetEnvironmentVariable("VOICEROID2_PATH");
        if (!string.IsNullOrEmpty(env) && File.Exists(env))
        {
            return env;
        }

        // 2. Windows Installer のアセンブリ登録 (VOICEROID2 の MSI が登録するキー)
        var installer = ProbeInstallerAssemblies();
        if (installer != null)
        {
            return installer;
        }

        // 3. Uninstall キーから InstallLocation を辿る (32/64bit 両方見る)
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            var path = ProbeUninstallKey(view);
            if (path != null)
            {
                return path;
            }
        }

        // 4. 既定インストールパス
        var defaults = new[]
        {
            @"C:\Program Files (x86)\AHS\VOICEROID2\VoiceroidEditor.exe",
            @"C:\Program Files\AHS\VOICEROID2\VoiceroidEditor.exe",
        };
        return defaults.FirstOrDefault(File.Exists);
    }

    private static string? ProbeInstallerAssemblies()
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(@"Installer\Assemblies");
            if (key == null) return null;

            // サブキー名はファイルパスの '\' が '|' に置換された形 (Windows Installer 仕様)
            var hit = key.GetSubKeyNames()
                .FirstOrDefault(n => n.EndsWith("|" + ExeName, StringComparison.OrdinalIgnoreCase)
                                     || n.EndsWith(ExeName, StringComparison.OrdinalIgnoreCase));
            return hit?.Replace('|', '\\');
        }
        catch
        {
            return null;
        }
    }

    private static string? ProbeUninstallKey(RegistryView view)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var uninst = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninst == null) return null;

            foreach (var name in uninst.GetSubKeyNames())
            {
                using var sub = uninst.OpenSubKey(name);
                var display = sub?.GetValue("DisplayName") as string;
                if (display == null || !display.Contains("VOICEROID")) continue;

                var location = sub?.GetValue("InstallLocation") as string;
                if (string.IsNullOrEmpty(location)) continue;

                var candidate = Path.Combine(location, ExeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch
        {
            // ignore - fall through to next strategy
        }
        return null;
    }
}
