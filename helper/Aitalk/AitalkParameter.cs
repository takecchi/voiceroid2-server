// Ported from https://github.com/Nkyoku/voiceroid_daemon (Aitalk/source/AitalkParameter.cs)
#nullable disable
using System;
using System.Linq;

namespace Voiceroid2Helper.Aitalk
{
    public class AitalkParameter
    {
        internal AitalkParameter(string voice_db_name, AitalkCore.TtsParam tts_param, AitalkCore.TtsParam.SpeakerParam[] speaker_params)
        {
            VoiceDbName = voice_db_name;
            TtsParam = tts_param;
            SpeakerParameters = speaker_params;
            CurrentSpeakerName = SpeakerParameters[0].VoiceName;
            CurrentSpeakerParameter = SpeakerParameters[0];
        }

        public bool IsParameterChanged { get; internal set; } = true;

        public int TextBufferCapacityInBytes => TtsParam.TextBufferCapacityInBytes;

        public int RawBufferCapacityInBytes => TtsParam.RawBufferCapacityInBytes;

        public bool AutoBookmark
        {
            get => (TtsParam.ExtendFormatFlags & AitalkCore.ExtendFormat.AutoBookmark) != 0;
            set
            {
                IsParameterChanged |= (value != AutoBookmark);
                if (value)
                {
                    TtsParam.ExtendFormatFlags |= AitalkCore.ExtendFormat.AutoBookmark;
                }
                else
                {
                    TtsParam.ExtendFormatFlags &= ~AitalkCore.ExtendFormat.AutoBookmark;
                }
            }
        }

        public bool JeitaRuby
        {
            get => (TtsParam.ExtendFormatFlags & AitalkCore.ExtendFormat.JeitaRuby) != 0;
            set
            {
                IsParameterChanged |= (value != JeitaRuby);
                if (value)
                {
                    TtsParam.ExtendFormatFlags |= AitalkCore.ExtendFormat.JeitaRuby;
                }
                else
                {
                    TtsParam.ExtendFormatFlags &= ~AitalkCore.ExtendFormat.JeitaRuby;
                }
            }
        }

        // マスター音量 (0-5)
        public double MasterVolume
        {
            get => TtsParam.Volume;
            set
            {
                float value_f = (float)Math.Max(MinMasterVolume, Math.Min(value, MaxMasterVolume));
                IsParameterChanged |= (value_f != TtsParam.Volume);
                TtsParam.Volume = value_f;
            }
        }
        public const double MaxMasterVolume = 5.0;
        public const double MinMasterVolume = 0.0;

        public string[] VoiceNames => SpeakerParameters.Select(x => x.VoiceName).ToArray();

        public string CurrentSpeakerName
        {
            get => TtsParam.VoiceName;
            set
            {
                if (TtsParam.VoiceName == value)
                {
                    return;
                }
                var speaker_parameter = SpeakerParameters.FirstOrDefault(x => x.VoiceName == value);
                if (speaker_parameter == null)
                {
                    throw new AitalkException($"話者'{value}'は存在しません。");
                }
                CurrentSpeakerParameter = speaker_parameter;
                TtsParam.VoiceName = value;
                IsParameterChanged = true;
            }
        }

        public double VoiceVolume
        {
            get => CurrentSpeakerParameter.Volume;
            set
            {
                float value_f = (float)Math.Max(MinVoiceVolume, Math.Min(value, MaxVoiceVolume));
                IsParameterChanged |= (value_f != CurrentSpeakerParameter.Volume);
                CurrentSpeakerParameter.Volume = value_f;
            }
        }
        public const double MinVoiceVolume = 0.0;
        public const double MaxVoiceVolume = 2.0;

        public double VoiceSpeed
        {
            get => CurrentSpeakerParameter.Speed;
            set
            {
                float value_f = (float)Math.Max(MinVoiceSpeed, Math.Min(value, MaxVoiceSpeed));
                IsParameterChanged |= (value_f != CurrentSpeakerParameter.Speed);
                CurrentSpeakerParameter.Speed = value_f;
            }
        }
        public const double MinVoiceSpeed = 0.5;
        public const double MaxVoiceSpeed = 4.0;

        public double VoicePitch
        {
            get => CurrentSpeakerParameter.Pitch;
            set
            {
                float value_f = (float)Math.Max(MinVoicePitch, Math.Min(value, MaxVoicePitch));
                IsParameterChanged |= (value_f != CurrentSpeakerParameter.Pitch);
                CurrentSpeakerParameter.Pitch = value_f;
            }
        }
        public const double MinVoicePitch = 0.5;
        public const double MaxVoicePitch = 2.0;

        public double VoiceEmphasis
        {
            get => CurrentSpeakerParameter.Range;
            set
            {
                float value_f = (float)Math.Max(MinVoiceEmphasis, Math.Min(value, MaxVoiceEmphasis));
                IsParameterChanged |= (value_f != CurrentSpeakerParameter.Range);
                CurrentSpeakerParameter.Range = value_f;
            }
        }
        public const double MinVoiceEmphasis = 0.0;
        public const double MaxVoiceEmphasis = 2.0;

        public int PauseMiddle
        {
            get => CurrentSpeakerParameter.PauseMiddle;
            set
            {
                value = Math.Max(MinPauseMiddle, Math.Min(value, MaxPauseMiddle));
                IsParameterChanged |= (value != CurrentSpeakerParameter.PauseMiddle);
                CurrentSpeakerParameter.PauseMiddle = value;
                if (PauseLong < value)
                {
                    PauseLong = value;
                }
            }
        }
        public const int MinPauseMiddle = 80;
        public const int MaxPauseMiddle = 500;

        public int PauseLong
        {
            get => CurrentSpeakerParameter.PauseLong;
            set
            {
                value = Math.Max(MinPauseLong, Math.Min(value, MaxPauseLong));
                IsParameterChanged |= (value != CurrentSpeakerParameter.PauseLong);
                CurrentSpeakerParameter.PauseLong = value;
                if (value < PauseMiddle)
                {
                    PauseMiddle = value;
                }
            }
        }
        public const int MinPauseLong = 100;
        public const int MaxPauseLong = 2000;

        public int PauseSentence
        {
            get => CurrentSpeakerParameter.PauseSentence;
            set
            {
                value = Math.Max(MinPauseSentence, Math.Min(value, MaxPauseSentence));
                IsParameterChanged |= (value != CurrentSpeakerParameter.PauseSentence);
                CurrentSpeakerParameter.PauseSentence = value;
            }
        }
        public const int MinPauseSentence = 0;
        public const int MaxPauseSentence = 10000;

        internal string VoiceDbName;
        internal AitalkCore.TtsParam TtsParam;
        internal AitalkCore.TtsParam.SpeakerParam[] SpeakerParameters;
        private AitalkCore.TtsParam.SpeakerParam CurrentSpeakerParameter;
    }
}
