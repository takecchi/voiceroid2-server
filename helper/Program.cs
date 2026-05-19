using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Voiceroid2Helper;

internal enum Command
{
    None,
    Help,
    ListSpeakers,
    Talk,
    Save,
}

internal static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            var parsed = ParseArgs(args);
            return parsed.Command switch
            {
                Command.Help => PrintHelp(),
                Command.ListSpeakers => HandleListSpeakers(),
                Command.Talk => HandleTalk(parsed),
                Command.Save => HandleSave(parsed),
                _ => PrintHelp(exitCode: 2),
            };
        }
        catch (Exception ex)
        {
            LogWriter.Error("helper failed", ex);
            return 1;
        }
    }

    private static int HandleListSpeakers()
    {
        using var driver = Voiceroid2Driver.AttachAndWaitReady();
        foreach (var name in driver.ListSpeakers())
        {
            Console.WriteLine(name);
        }
        return 0;
    }

    private static int HandleTalk(ParsedArgs args)
    {
        using var driver = Voiceroid2Driver.AttachAndWaitReady();
        driver.Talk(args.RequireText(), args.Speaker);
        return 0;
    }

    private static int HandleSave(ParsedArgs args)
    {
        using var driver = Voiceroid2Driver.AttachAndWaitReady();
        driver.SaveAudio(args.RequireText(), args.Speaker, args.RequireOut());
        return 0;
    }

    private static int PrintHelp(int exitCode = 0)
    {
        Console.WriteLine(
            "voiceroid2-helper\n" +
            "  --list-speakers\n" +
            "      List speakers found on the main window.\n" +
            "  --talk --text TEXT [--speaker NAME]\n" +
            "      Play TEXT through the system speaker.\n" +
            "  --save --text TEXT --out FILE [--speaker NAME]\n" +
            "      Synthesize TEXT and write a WAV to FILE via VOICEROID2's save flow.\n");
        return exitCode;
    }

    private sealed class ParsedArgs
    {
        public Command Command { get; set; } = Command.None;
        public string? Text { get; set; }
        public string? Out { get; set; }
        public string? Speaker { get; set; }

        public string RequireText() =>
            string.IsNullOrEmpty(Text)
                ? throw new ArgumentException("--text is required")
                : Text!;

        public string RequireOut() =>
            string.IsNullOrEmpty(Out)
                ? throw new ArgumentException("--out is required")
                : Out!;
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
                case "--speaker":
                    result.Speaker = RequireValue(args, ref i, "--speaker");
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
}
