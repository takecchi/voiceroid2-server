namespace Voiceroid2Helper;

/// <summary>
/// VOICEROID2 マスター効果パラメータの倍率セット (音量・話速・高さ・抑揚)。
/// すべて 1.0 が VOICEROID2 既定値。
///
/// プロセス越しの状態として VOICEROID2 にはこれら 4 値がスティックするので、
/// 直前リクエストの設定が次に漏れないよう helper では呼び出しごとに必ず明示的に
/// 4 値をセットする (CLI 側からも常に 4 値を渡す前提)。
/// </summary>
internal readonly struct VoiceTuning
{
    public double Volume { get; }
    public double Speed { get; }
    public double Pitch { get; }
    public double Intonation { get; }

    public VoiceTuning(double volume, double speed, double pitch, double intonation)
    {
        Volume = volume;
        Speed = speed;
        Pitch = pitch;
        Intonation = intonation;
    }

    public static VoiceTuning Default { get; } = new VoiceTuning(1.0, 1.0, 1.0, 1.0);

    public VoiceTuning WithVolume(double v) => new VoiceTuning(v, Speed, Pitch, Intonation);
    public VoiceTuning WithSpeed(double v) => new VoiceTuning(Volume, v, Pitch, Intonation);
    public VoiceTuning WithPitch(double v) => new VoiceTuning(Volume, Speed, v, Intonation);
    public VoiceTuning WithIntonation(double v) => new VoiceTuning(Volume, Speed, Pitch, v);
}
