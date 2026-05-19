// helper の新フロー (DLL 直叩き)
//
// 旧 helper (Codeer.Friendly で VOICEROID2 エディタを GUI 操作する) は、
// voiceroid_daemon (https://github.com/Nkyoku/voiceroid_daemon) と同じく
// aitalked.dll を P/Invoke で直接叩く方式へ置き換えた。
// これにより以下の問題が消える:
//   - VOICEROID2 エディタの起動が不要
//   - WPF Binding 名のバージョン差異
//   - SaveFileDialog の自動操作と Win32 ダイアログ追尾
//   - 半角/全角 '>' による話者切替記号
// ただし VOICEROID2 が「マシン固定ライセンスのアクティベーション済み環境」である前提は同じ。
//
// 初回 1 回だけ --get-key で VOICEROID2 エディタを起動し、認証コードシード (LicenseKey)
// を抜き出して環境変数 / 引数経由で渡す運用にする。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using Voiceroid2Helper.Aitalk;

namespace Voiceroid2Helper;

internal enum Command
{
    None,
    Help,
    GetKey,
    ListVoiceDbs,
    ListSpeakers,
    Talk,
    Save,
}

internal static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // 終了コード規約 (API 側はこれを HTTP ステータスにマップする):
        //   0 ... 成功
        //   1 ... 内部エラー (バグ、DLL 例外、タイムアウト等)
        //   2 ... ユーザー入力起因のエラー (未知の voice_db / voice_name、CLI 引数不正)
        //   3 ... サーバー設定起因のエラー (認証コード不正、ライセンス期限切れ等)
        try
        {
            var parsed = ParseArgs(args);
            return parsed.Command switch
            {
                Command.Help => PrintHelp(),
                Command.GetKey => HandleGetKey(),
                Command.ListVoiceDbs => HandleListVoiceDbs(parsed),
                Command.ListSpeakers => HandleListSpeakers(parsed),
                Command.Talk => HandleTalk(parsed),
                Command.Save => HandleSave(parsed),
                _ => PrintHelp(exitCode: 2),
            };
        }
        catch (AitalkException ex)
        {
            LogWriter.Error("aitalk failure", ex);
            return ex.Kind switch
            {
                AitalkErrorKind.UserInput => 2,
                AitalkErrorKind.ServerConfig => 3,
                _ => 1,
            };
        }
        catch (ArgumentException ex)
        {
            LogWriter.Error("invalid argument", ex);
            return 2;
        }
        catch (Exception ex)
        {
            LogWriter.Error("helper failed", ex);
            return 1;
        }
    }

    private static int HandleGetKey()
    {
        var key = KeyExtractor.GetKey();
        if (string.IsNullOrEmpty(key))
        {
            LogWriter.Error("VoiceroidEditor が起動していないか、認証コードを取得できませんでした。");
            return 1;
        }
        Console.WriteLine(key);
        return 0;
    }

    private static int HandleListVoiceDbs(ParsedArgs args)
    {
        // DLL 初期化なしで Voice/ フォルダだけスキャンする (高速)
        var voiceDir = Path.Combine(args.RequireInstallDir(), "Voice");
        if (!Directory.Exists(voiceDir))
        {
            throw new InvalidOperationException($"Voice ディレクトリが存在しません: {voiceDir}");
        }
        foreach (var path in Directory.GetDirectories(voiceDir).OrderBy(p => p, StringComparer.InvariantCultureIgnoreCase))
        {
            Console.WriteLine(Path.GetFileName(path));
        }
        return 0;
    }

    private static int HandleListSpeakers(ParsedArgs args)
    {
        InitializeAitalk(args);
        AitalkWrapper.LoadLanguage(args.Language);
        AitalkWrapper.LoadVoice(args.RequireVoiceDb());
        foreach (var name in AitalkWrapper.Parameter.VoiceNames)
        {
            Console.WriteLine(name);
        }
        return 0;
    }

    private static int HandleTalk(ParsedArgs args)
    {
        var wav = SynthesizeToBuffer(args);
        using var ms = new MemoryStream(wav);
        using var player = new SoundPlayer(ms);
        player.PlaySync();
        return 0;
    }

    private static int HandleSave(ParsedArgs args)
    {
        var wav = SynthesizeToBuffer(args);
        var outPath = args.RequireOut();
        // 親ディレクトリが無いケースのほうがバグっぽいので作らない (NestJS 側で必ず親を作る)
        File.WriteAllBytes(outPath, wav);
        return 0;
    }

    private static byte[] SynthesizeToBuffer(ParsedArgs args)
    {
        var text = args.RequireText();
        InitializeAitalk(args);
        AitalkWrapper.LoadLanguage(args.Language);
        AitalkWrapper.LoadVoice(args.RequireVoiceDb());

        if (!string.IsNullOrEmpty(args.VoiceName))
        {
            AitalkWrapper.Parameter.CurrentSpeakerName = args.VoiceName!;
        }

        // マスター効果 (全話者共通)
        AitalkWrapper.Parameter.MasterVolume = args.Volume;
        // 話者ごとのパラメータ
        AitalkWrapper.Parameter.VoiceSpeed = args.Speed;
        AitalkWrapper.Parameter.VoicePitch = args.Pitch;
        AitalkWrapper.Parameter.VoiceEmphasis = args.Intonation;

        var kana = AitalkWrapper.TextToKana(text, args.KanaTimeoutMs);
        using var ms = new MemoryStream();
        AitalkWrapper.KanaToSpeech(kana, ms, args.SpeechTimeoutMs);
        return ms.ToArray();
    }

    private static void InitializeAitalk(ParsedArgs args)
    {
        AitalkWrapper.Initialize(args.RequireInstallDir(), args.RequireAuthCode());
    }

    private static int PrintHelp(int exitCode = 0)
    {
        Console.WriteLine(
            "voiceroid2-helper (DLL direct)\n" +
            "  --get-key\n" +
            "      VoiceroidEditor 起動中なら認証コードシードを stdout に書き出す。\n" +
            "  --list-voice-dbs --install-dir DIR\n" +
            "      <install-dir>\\Voice/ 配下のボイスライブラリ名を 1 行ずつ出力。\n" +
            "  --list-speakers --install-dir DIR --auth-code CODE --voice-db NAME [--language standard]\n" +
            "      指定ボイスライブラリ内の話者名を 1 行ずつ出力。\n" +
            "  --talk --text TEXT --install-dir DIR --auth-code CODE --voice-db NAME [--voice-name NAME] [tuning]\n" +
            "      合成して既定のスピーカーから再生する (.NET SoundPlayer 同期再生)。\n" +
            "  --save --text TEXT --out FILE --install-dir DIR --auth-code CODE --voice-db NAME [--voice-name NAME] [tuning]\n" +
            "      合成して WAV (44.1kHz/16bit/mono) をファイルに書き出す。\n" +
            "  tuning options (default 1.0):\n" +
            "      --volume N      マスター音量 (0-5)\n" +
            "      --speed  N      話速 (0.5-4)\n" +
            "      --pitch  N      高さ (0.5-2)\n" +
            "      --intonation N  抑揚 (0-2)\n" +
            "  other:\n" +
            "      --language NAME 言語 (default: standard)\n" +
            "      --kana-timeout-ms N    仮名変換タイムアウト (default 0 = wait forever)\n" +
            "      --speech-timeout-ms N  音声変換タイムアウト (default 0 = wait forever)\n");
        return exitCode;
    }

    private sealed class ParsedArgs
    {
        public Command Command { get; set; } = Command.None;
        public string? Text { get; set; }
        public string? Out { get; set; }
        public string? InstallDir { get; set; }
        public string? AuthCode { get; set; }
        public string? VoiceDb { get; set; }
        public string? VoiceName { get; set; }
        public string Language { get; set; } = "standard";
        public double Volume { get; set; } = 1.0;
        public double Speed { get; set; } = 1.0;
        public double Pitch { get; set; } = 1.0;
        public double Intonation { get; set; } = 1.0;
        public int KanaTimeoutMs { get; set; } = 0;
        public int SpeechTimeoutMs { get; set; } = 0;

        public string RequireText() => Require(Text, "--text");
        public string RequireOut() => Require(Out, "--out");
        public string RequireInstallDir() => Require(InstallDir, "--install-dir");
        public string RequireAuthCode() => Require(AuthCode, "--auth-code");
        public string RequireVoiceDb() => Require(VoiceDb, "--voice-db");

        private static string Require(string? value, string flag) =>
            string.IsNullOrEmpty(value)
                ? throw new ArgumentException($"{flag} is required")
                : value!;
    }

    private static ParsedArgs ParseArgs(string[] args)
    {
        var result = new ParsedArgs();
        if (args.Length == 0)
        {
            result.Command = Command.Help;
            return result;
        }

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help":
                case "-h":
                    result.Command = Command.Help;
                    return result;
                case "--get-key":
                    result.Command = Command.GetKey;
                    break;
                case "--list-voice-dbs":
                    result.Command = Command.ListVoiceDbs;
                    break;
                case "--list-speakers":
                    result.Command = Command.ListSpeakers;
                    break;
                case "--talk":
                    result.Command = Command.Talk;
                    break;
                case "--save":
                    result.Command = Command.Save;
                    break;
                case "--text":
                    result.Text = RequireValue(args, ref i, "--text");
                    break;
                case "--out":
                    result.Out = RequireValue(args, ref i, "--out");
                    break;
                case "--install-dir":
                    result.InstallDir = RequireValue(args, ref i, "--install-dir");
                    break;
                case "--auth-code":
                    result.AuthCode = RequireValue(args, ref i, "--auth-code");
                    break;
                case "--voice-db":
                    result.VoiceDb = RequireValue(args, ref i, "--voice-db");
                    break;
                case "--voice-name":
                    result.VoiceName = RequireValue(args, ref i, "--voice-name");
                    break;
                case "--language":
                    result.Language = RequireValue(args, ref i, "--language");
                    break;
                case "--volume":
                    result.Volume = ParseDouble(args, ref i, "--volume");
                    break;
                case "--speed":
                    result.Speed = ParseDouble(args, ref i, "--speed");
                    break;
                case "--pitch":
                    result.Pitch = ParseDouble(args, ref i, "--pitch");
                    break;
                case "--intonation":
                    result.Intonation = ParseDouble(args, ref i, "--intonation");
                    break;
                case "--kana-timeout-ms":
                    result.KanaTimeoutMs = ParseInt(args, ref i, "--kana-timeout-ms");
                    break;
                case "--speech-timeout-ms":
                    result.SpeechTimeoutMs = ParseInt(args, ref i, "--speech-timeout-ms");
                    break;
                default:
                    throw new ArgumentException($"unknown argument: {args[i]}");
            }
        }

        return result;
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
        {
            throw new ArgumentException($"{flag} requires a value");
        }
        return args[++i];
    }

    private static double ParseDouble(string[] args, ref int i, string flag)
    {
        var raw = RequireValue(args, ref i, flag);
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            throw new ArgumentException($"{flag} requires a numeric value (got \"{raw}\")");
        }
        return value;
    }

    private static int ParseInt(string[] args, ref int i, string flag)
    {
        var raw = RequireValue(args, ref i, flag);
        if (!int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            throw new ArgumentException($"{flag} requires an integer value (got \"{raw}\")");
        }
        return value;
    }
}
