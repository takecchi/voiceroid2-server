using System;

namespace Voiceroid2Helper;

/// <summary>
/// stderr 専用ロガー。stdout は API への構造化レスポンス用に予約する。
/// </summary>
internal static class LogWriter
{
    public static void Info(string msg) => Emit("info", msg);

    public static void Warn(string msg) => Emit("warn", msg);

    public static void Error(string msg) => Emit("error", msg);

    public static void Error(string msg, Exception ex) =>
        Emit("error", $"{msg} :: {ex.GetType().Name}: {ex.Message}");

    private static void Emit(string level, string msg)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss.fff");
        Console.Error.WriteLine($"{ts} {level,-5} {msg}");
    }
}
