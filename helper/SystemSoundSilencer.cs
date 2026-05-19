using System;
using System.IO;
using Microsoft.Win32;

namespace Voiceroid2Helper;

/// <summary>
/// Windows の「情報」システムサウンド (SystemAsterisk) を helper 実行中だけ一時的に
/// 無音化する。VOICEROID2 の音声保存完了 MessageBox には Information アイコンが付くため
/// OS が自動的に PlaySound("SystemAsterisk") を呼んでしまい、保存のたびに "ぽこーん" が
/// 鳴る。これを抑止する。
///
/// 仕組み:
///   - HKCU\AppEvents\Schemes\Apps\.Default\SystemAsterisk\.Current の (Default) 値
///     (= 音源 wav へのパス) を一時的に空文字に書き換える。
///   - 元の値はバックアップファイル (%TEMP%\voiceroid2-helper-asterisk-backup.txt) に
///     保存し、Dispose で読み戻して原状復帰 + バックアップ削除。
///   - helper が外部 kill 等で Dispose を通らずに終わった場合に備えて、起動時に
///     RestoreLeftovers() でバックアップファイルが残っていれば復元する。
///
/// 副作用: 本クラスがアクティブな間、OS 全体の「情報」音が消える。
/// helper の単発呼び出しは数秒なので実害は限定的。
/// </summary>
internal sealed class SystemSoundSilencer : IDisposable
{
    private const string SubKeyPath = @"AppEvents\Schemes\Apps\.Default\SystemAsterisk\.Current";

    private static readonly string BackupFilePath = Path.Combine(
        Path.GetTempPath(), "voiceroid2-helper-asterisk-backup.txt");

    private readonly string? _originalValue;
    private readonly bool _muted;
    private bool _restored;

    /// <summary>
    /// 前回 helper がクラッシュ等で Dispose を通らずに終わったときに残ったバックアップを
    /// 起動時に拾って原状復帰する。helper の Main 冒頭から呼ぶ想定。
    /// </summary>
    public static void RestoreLeftovers()
    {
        try
        {
            if (!File.Exists(BackupFilePath)) return;
            var original = File.ReadAllText(BackupFilePath);
            using var key = Registry.CurrentUser.OpenSubKey(SubKeyPath, writable: true);
            key?.SetValue(null, original, RegistryValueKind.ExpandString);
            File.Delete(BackupFilePath);
            LogWriter.Info("SystemSoundSilencer: restored leftover SystemAsterisk from prior run");
        }
        catch (Exception ex)
        {
            LogWriter.Warn(
                $"SystemSoundSilencer.RestoreLeftovers failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public SystemSoundSilencer()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SubKeyPath, writable: true);
            if (key == null)
            {
                LogWriter.Warn($"SystemAsterisk key not found at HKCU\\{SubKeyPath}");
                return;
            }
            _originalValue = key.GetValue(null) as string ?? "";
            // 書き換え前にバックアップを永続化 (順序重要: 書き換え後に死ぬと永久ミュート化する)
            File.WriteAllText(BackupFilePath, _originalValue);
            key.SetValue(null, "", RegistryValueKind.ExpandString);
            _muted = true;
        }
        catch (Exception ex)
        {
            LogWriter.Warn(
                $"SystemSoundSilencer.ctor failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (!_muted || _restored) return;
        _restored = true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SubKeyPath, writable: true);
            key?.SetValue(null, _originalValue ?? "", RegistryValueKind.ExpandString);
        }
        catch (Exception ex)
        {
            LogWriter.Warn(
                $"SystemSoundSilencer.Dispose registry restore failed: {ex.GetType().Name}: {ex.Message}");
        }
        try
        {
            File.Delete(BackupFilePath);
        }
        catch
        {
            // バックアップ削除失敗は実害ないので無視 (次回起動の RestoreLeftovers で空文字を
            // 書き戻すだけ。元の値が既に戻っているので冪等)
        }
    }
}
