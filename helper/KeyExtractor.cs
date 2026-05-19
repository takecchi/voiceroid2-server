// Ported from https://github.com/Nkyoku/voiceroid_daemon (voiceroidd/Injecter.cs)
// VoiceroidEditor.exe にアタッチして AI.Framework.AppFramework.Current.AppSettings.LicenseKey を抜き出す。
// この値が aitalked.dll の Init() に渡す AuthenticateCodeSeed になる。
// 初回 1 回だけ実行できれば良い (取得後は設定として永続化する想定)。
using System;
using System.Diagnostics;
using System.Reflection;
using Codeer.Friendly.Dynamic;
using Codeer.Friendly.Windows;

namespace Voiceroid2Helper
{
    internal static class KeyExtractor
    {
        /// <summary>
        /// VOICEROID2 エディタ起動中なら認証コードシードを返す。
        /// 起動していなければ null。
        /// </summary>
        public static string? GetKey()
        {
            var processes = Process.GetProcessesByName("VoiceroidEditor");
            if (processes.Length == 0)
            {
                return null;
            }
            var process = processes[0];

            // 注意: helper と VoiceroidEditor の権限 (UAC レベル) が一致していないとアタッチに失敗する。
            // CLAUDE.md の "権限を揃える" 方針を守ること。
            var app = new WindowsAppFriend(process);
            WindowsAppExpander.LoadAssembly(app, typeof(KeyExtractor).Assembly);
            dynamic injected = app.Type(typeof(KeyExtractor));
            try
            {
                return (string?)injected.InjectedGetKey();
            }
            catch (Exception)
            {
                return null;
            }
        }

        // VoiceroidEditor 内で実行されるコード
        private static string? InjectedGetKey()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == "AI.Framework.App")
                {
                    var type = assembly.GetType("AI.Framework.AppFramework");
                    var property = type?.GetProperty("Current");
                    if (property == null)
                    {
                        return null;
                    }
                    dynamic current = property.GetValue(type)!;
                    return (string?)current.AppSettings.LicenseKey;
                }
            }
            return null;
        }
    }
}
