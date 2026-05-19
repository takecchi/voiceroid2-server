// Ported from https://github.com/Nkyoku/voiceroid_daemon (Aitalk/source/AitalkWrapper.cs)
// helper でそのまま動かすため、ジョブイベント JSON の phon チャンク埋め込みは省略している
// (NestJS 側ではこの phon チャンクは使っていない)。完全互換を取りたくなったら復活させる。
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Voiceroid2Helper.Aitalk
{
    public static class AitalkWrapper
    {
        /// <summary>
        /// AITalk を初期化する。
        /// </summary>
        /// <param name="install_directory">VOICEROID2 のインストールディレクトリ</param>
        /// <param name="authenticate_code">認証コードシード</param>
        public static void Initialize(string install_directory, string authenticate_code)
        {
            Finish();

            if ((InstallDirectory != null) && (InstallDirectory != install_directory))
            {
                throw new AitalkException("インストールディレクトリを変更して再び初期化することはできません。");
            }
            InstallDirectory = install_directory;
            SetDllDirectory(InstallDirectory);

            AitalkCore.Config config;
            config.VoiceDbSampleRate = VoiceSampleRate;
            config.VoiceDbDirectory = $"{InstallDirectory}\\Voice";
            config.TimeoutMilliseconds = TimeoutMilliseconds;
            config.LicensePath = $"{InstallDirectory}\\aitalk.lic";
            config.AuthenticateCodeSeed = authenticate_code;
            config.ReservedZero = 0;

            AitalkCore.Result result;
            try
            {
                result = AitalkCore.Init(ref config);
            }
            catch (Exception e)
            {
                throw new AitalkException("AITalk の初期化に失敗しました。", e);
            }
            if (result != AitalkCore.Result.Success)
            {
                throw new AitalkException("AITalk の初期化に失敗しました。", result);
            }
            IsInitialized = true;
        }

        public static void Finish()
        {
            if (IsInitialized)
            {
                IsInitialized = false;
                AitalkCore.End();
            }
            CurrentLanguage = null;
            CurrentVoice = null;
        }

        public static string[] LanguageList
        {
            get
            {
                var result = new List<string>();
                try
                {
                    foreach (string path in Directory.GetDirectories($"{InstallDirectory}\\Lang"))
                    {
                        result.Add(Path.GetFileName(path));
                    }
                }
                catch (Exception) { }
                result.Sort(StringComparer.InvariantCultureIgnoreCase);
                return result.ToArray();
            }
        }

        public static string[] VoiceDbList
        {
            get
            {
                var result = new List<string>();
                try
                {
                    foreach (string path in Directory.GetDirectories($"{InstallDirectory}\\Voice"))
                    {
                        result.Add(Path.GetFileName(path));
                    }
                }
                catch (Exception) { }
                result.Sort(StringComparer.InvariantCultureIgnoreCase);
                return result.ToArray();
            }
        }

        public static void LoadLanguage(string language_name)
        {
            if (language_name == CurrentLanguage)
            {
                return;
            }
            // LangLoad はカレントディレクトリが install_dir 以外だと失敗する。
            string current_directory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(InstallDirectory);
            CurrentLanguage = null;
            AitalkCore.Result result;
            result = AitalkCore.LangClear();
            if ((result == AitalkCore.Result.Success) || (result == AitalkCore.Result.NotLoaded))
            {
                result = AitalkCore.LangLoad($"{InstallDirectory}\\Lang\\{language_name}");
            }
            Directory.SetCurrentDirectory(current_directory);
            if (result != AitalkCore.Result.Success)
            {
                throw new AitalkException($"言語'{language_name}'の読み込みに失敗しました。", result);
            }
            CurrentLanguage = language_name;
        }

        public static void ReloadPhraseDictionary(string path)
        {
            AitalkCore.ReloadPhraseDic(null);
            if (path == null)
            {
                return;
            }
            var result = AitalkCore.ReloadPhraseDic(path);
            if (result == AitalkCore.Result.UserDictionaryNoEntry)
            {
                AitalkCore.ReloadPhraseDic(null);
            }
            else if (result != AitalkCore.Result.Success)
            {
                throw new AitalkException($"フレーズ辞書'{path}'の読み込みに失敗しました。", result);
            }
        }

        public static void ReloadWordDictionary(string path)
        {
            AitalkCore.ReloadWordDic(null);
            if (path == null)
            {
                return;
            }
            var result = AitalkCore.ReloadWordDic(path);
            if (result == AitalkCore.Result.UserDictionaryNoEntry)
            {
                AitalkCore.ReloadWordDic(null);
            }
            else if (result != AitalkCore.Result.Success)
            {
                throw new AitalkException($"単語辞書'{path}'の読み込みに失敗しました。", result);
            }
        }

        public static void ReloadSymbolDictionary(string path)
        {
            AitalkCore.ReloadSymbolDic(null);
            if (path == null)
            {
                return;
            }
            var result = AitalkCore.ReloadSymbolDic(path);
            if (result == AitalkCore.Result.UserDictionaryNoEntry)
            {
                AitalkCore.ReloadSymbolDic(null);
            }
            else if (result != AitalkCore.Result.Success)
            {
                throw new AitalkException($"記号ポーズ辞書'{path}'の読み込みに失敗しました。", result);
            }
        }

        public static void LoadVoice(string voice_db_name)
        {
            if (voice_db_name == CurrentVoice)
            {
                return;
            }

            CurrentVoice = null;
            AitalkCore.VoiceClear();
            if (voice_db_name == null)
            {
                return;
            }
            var result = AitalkCore.VoiceLoad(voice_db_name);
            if (result != AitalkCore.Result.Success)
            {
                throw new AitalkException($"ボイスライブラリ'{voice_db_name}'の読み込みに失敗しました。", result);
            }

            GetParameters(out var tts_param, out var speaker_params);
            tts_param.TextBufferCallback = TextBufferCallback;
            tts_param.RawBufferCallback = RawBufferCallback;
            tts_param.TtsEventCallback = TtsEventCallback;
            tts_param.PauseBegin = 0;
            tts_param.PauseTerm = 0;
            tts_param.ExtendFormatFlags = AitalkCore.ExtendFormat.JeitaRuby | AitalkCore.ExtendFormat.AutoBookmark;
            Parameter = new AitalkParameter(voice_db_name, tts_param, speaker_params);

            CurrentVoice = voice_db_name;
        }

        private static void GetParameters(out AitalkCore.TtsParam tts_param, out AitalkCore.TtsParam.SpeakerParam[] speaker_params)
        {
            AitalkCore.Result result;
            int size = 0;
            result = AitalkCore.GetParam(IntPtr.Zero, ref size);
            if ((result != AitalkCore.Result.Insufficient) || (size < Marshal.SizeOf<AitalkCore.TtsParam>()))
            {
                throw new AitalkException("動作パラメータの長さの取得に失敗しました。", result);
            }

            IntPtr ptr = Marshal.AllocCoTaskMem(size);
            try
            {
                Marshal.WriteInt32(ptr, (int)Marshal.OffsetOf<AitalkCore.TtsParam>("Size"), size);
                result = AitalkCore.GetParam(ptr, ref size);
                if (result != AitalkCore.Result.Success)
                {
                    throw new AitalkException("動作パラメータの取得に失敗しました。", result);
                }
                tts_param = Marshal.PtrToStructure<AitalkCore.TtsParam>(ptr);

                speaker_params = new AitalkCore.TtsParam.SpeakerParam[tts_param.NumberOfSpeakers];
                for (int index = 0; index < speaker_params.Length; index++)
                {
                    IntPtr speaker_ptr = IntPtr.Add(ptr, Marshal.SizeOf<AitalkCore.TtsParam>() + Marshal.SizeOf<AitalkCore.TtsParam.SpeakerParam>() * index);
                    speaker_params[index] = Marshal.PtrToStructure<AitalkCore.TtsParam.SpeakerParam>(speaker_ptr);
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(ptr);
            }
        }

        private static void SetParameters(AitalkCore.TtsParam tts_param, AitalkCore.TtsParam.SpeakerParam[] speaker_params)
        {
            int size = Marshal.SizeOf<AitalkCore.TtsParam>() + Marshal.SizeOf<AitalkCore.TtsParam.SpeakerParam>() * speaker_params.Length;
            IntPtr ptr = Marshal.AllocCoTaskMem(size);
            try
            {
                tts_param.Size = size;
                tts_param.NumberOfSpeakers = speaker_params.Length;
                Marshal.StructureToPtr<AitalkCore.TtsParam>(tts_param, ptr, false);
                for (int index = 0; index < speaker_params.Length; index++)
                {
                    IntPtr speaker_ptr = IntPtr.Add(ptr, Marshal.SizeOf<AitalkCore.TtsParam>() + Marshal.SizeOf<AitalkCore.TtsParam.SpeakerParam>() * index);
                    Marshal.StructureToPtr<AitalkCore.TtsParam.SpeakerParam>(speaker_params[index], speaker_ptr, false);
                }
                var result = AitalkCore.SetParam(ptr);
                if (result != AitalkCore.Result.Success)
                {
                    throw new AitalkException("動作パラメータの設定に失敗しました。", result);
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(ptr);
            }
        }

        private static void UpdateParameter()
        {
            if (Parameter.IsParameterChanged)
            {
                SetParameters(Parameter.TtsParam, Parameter.SpeakerParameters);
                Parameter.IsParameterChanged = false;
            }
        }

        public static string TextToKana(string text, int timeout = 0)
        {
            UpdateParameter();
            UnicodeToShiftJis(text, out byte[] shiftjis_bytes, out int[] shiftjis_positions);

            var job_data = new KanaJobData
            {
                BufferCapacity = 0x1000,
                Output = new List<byte>(),
                CloseEvent = new EventWaitHandle(false, EventResetMode.ManualReset),
            };
            GCHandle gc_handle = GCHandle.Alloc(job_data);
            try
            {
                AitalkCore.JobParam job_param;
                job_param.ModeInOut = AitalkCore.JobInOut.PlainToKana;
                job_param.UserData = GCHandle.ToIntPtr(gc_handle);
                var result = AitalkCore.TextToKana(out int job_id, ref job_param, shiftjis_bytes);
                if (result != AitalkCore.Result.Success)
                {
                    throw new AitalkException($"仮名変換が開始できませんでした。[{string.Join(",", shiftjis_bytes)}]", result);
                }

                bool respond = job_data.CloseEvent.WaitOne((0 < timeout) ? timeout : -1);
                result = AitalkCore.CloseKana(job_id);
                if (!respond)
                {
                    throw new AitalkException("仮名変換がタイムアウトしました。");
                }
                else if (result != AitalkCore.Result.Success)
                {
                    throw new AitalkException("仮名変換が正常に終了しませんでした。", result);
                }
            }
            finally
            {
                gc_handle.Free();
            }

            Encoding encoding = Encoding.GetEncoding(932);
            return ReplaceIrqMark(encoding.GetString(job_data.Output.ToArray()), shiftjis_positions);
        }

        private static void UnicodeToShiftJis(string unicode_string, out byte[] shiftjis_string, out int[] shiftjis_positions)
        {
            Encoding encoding = Encoding.GetEncoding(932);
            byte[] shiftjis_string_internal = encoding.GetBytes(unicode_string);
            int shiftjis_length = shiftjis_string_internal.Length;
            shiftjis_positions = new int[shiftjis_length + 1];
            char[] unicode_char_array = unicode_string.ToArray();
            int[] unicode_indexes = StringInfo.ParseCombiningCharacters(unicode_string);
            int char_count = unicode_indexes.Length;
            int shiftjis_index = 0;
            for (int char_index = 0; char_index < char_count; char_index++)
            {
                int unicode_index = unicode_indexes[char_index];
                int unicode_count = (((char_index + 1) < char_count) ? unicode_indexes[char_index + 1] : unicode_string.Length) - unicode_index;
                int shiftjis_count = encoding.GetByteCount(unicode_char_array, unicode_index, unicode_count);
                for (int offset = 0; offset < shiftjis_count; offset++)
                {
                    shiftjis_positions[shiftjis_index + offset] = char_index;
                }
                shiftjis_index += shiftjis_count;
            }
            shiftjis_positions[shiftjis_length] = char_count;

            shiftjis_string = new byte[shiftjis_length + 1];
            Buffer.BlockCopy(shiftjis_string_internal, 0, shiftjis_string, 0, shiftjis_length);
            shiftjis_string[shiftjis_length] = 0;
        }

        private static string ReplaceIrqMark(string input, int[] shiftjis_positions)
        {
            var output = new StringBuilder();
            int shiftjis_length = shiftjis_positions.Length;
            int index = 0;
            const string StartOfIrqMark = "(Irq MARK=_AI@";
            const string EndOfIrqMask = ")";
            while (true)
            {
                int start_pos = input.IndexOf(StartOfIrqMark, index);
                if (start_pos < 0)
                {
                    output.Append(input, index, input.Length - index);
                    break;
                }
                start_pos += StartOfIrqMark.Length;
                output.Append(input, index, start_pos - index);
                int end_pos = input.IndexOf(EndOfIrqMask, start_pos);
                if (end_pos < 0)
                {
                    output.Append(input, index, input.Length - start_pos);
                    break;
                }
                if (!int.TryParse(input.Substring(start_pos, end_pos - start_pos), out int shiftjis_index))
                {
                    throw new AitalkException("文節位置の取得に失敗しました。");
                }
                if ((shiftjis_index < 0) || (shiftjis_length <= shiftjis_index))
                {
                    throw new AitalkException("文節位置の特定に失敗しました。");
                }
                output.Append(shiftjis_positions[shiftjis_index]);
                output.Append(EndOfIrqMask);
                index = end_pos + EndOfIrqMask.Length;
            }
            return output.ToString();
        }

        private static int TextBufferCallback(AitalkCore.EventReason reason, int job_id, IntPtr user_data)
        {
            GCHandle gc_handle = GCHandle.FromIntPtr(user_data);
            KanaJobData job_data = gc_handle.Target as KanaJobData;
            if (job_data == null)
            {
                return 0;
            }

            int buffer_capacity = job_data.BufferCapacity;
            byte[] buffer = new byte[buffer_capacity];
            AitalkCore.Result result;
            int read_bytes;
            do
            {
                result = AitalkCore.GetKana(job_id, buffer, buffer_capacity, out read_bytes, out _);
                if (result != AitalkCore.Result.Success)
                {
                    break;
                }
                job_data.Output.AddRange(new ArraySegment<byte>(buffer, 0, read_bytes));
            }
            while ((buffer_capacity - 1) <= read_bytes);
            if (reason == AitalkCore.EventReason.TextBufferClose)
            {
                job_data.CloseEvent.Set();
            }
            return 0;
        }

        /// <summary>
        /// 読み仮名 → WAV (44.1kHz/16bit/mono) を wave_stream に書き出す。
        /// </summary>
        public static void KanaToSpeech(string kana, Stream wave_stream, int timeout = 0)
        {
            UpdateParameter();

            var job_data = new SpeechJobData
            {
                BufferCapacity = 176400,
                Output = new List<byte>(),
                CloseEvent = new EventWaitHandle(false, EventResetMode.ManualReset),
            };
            GCHandle gc_handle = GCHandle.Alloc(job_data);
            try
            {
                AitalkCore.JobParam job_param;
                job_param.ModeInOut = AitalkCore.JobInOut.KanaToWave;
                job_param.UserData = GCHandle.ToIntPtr(gc_handle);
                var result = AitalkCore.TextToSpeech(out int job_id, ref job_param, kana);
                if (result != AitalkCore.Result.Success)
                {
                    throw new AitalkException("音声変換が開始できませんでした。", result);
                }

                bool respond = job_data.CloseEvent.WaitOne((0 < timeout) ? timeout : -1);
                result = AitalkCore.CloseSpeech(job_id);
                if (!respond)
                {
                    throw new AitalkException("音声変換がタイムアウトしました。");
                }
                else if (result != AitalkCore.Result.Success)
                {
                    throw new AitalkException("音声変換が正常に終了しませんでした。", result);
                }
            }
            finally
            {
                gc_handle.Free();
            }

            // 標準 RIFF WAV (44.1kHz / 16bit / mono)。phon チャンク (TTS イベント) は省略。
            byte[] data = job_data.Output.ToArray();
            var writer = new BinaryWriter(wave_stream);
            writer.Write(new byte[4] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            writer.Write(36 + data.Length);
            writer.Write(new byte[4] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
            writer.Write(new byte[4] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            writer.Write(16);
            writer.Write((short)0x1);
            writer.Write((short)1);
            writer.Write(VoiceSampleRate);
            writer.Write(2 * VoiceSampleRate);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(new byte[4] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            writer.Write(data.Length);
            writer.Write(data);
        }

        private static int RawBufferCallback(AitalkCore.EventReason reason, int job_id, long tick, IntPtr user_data)
        {
            GCHandle gc_handle = GCHandle.FromIntPtr(user_data);
            SpeechJobData job_data = gc_handle.Target as SpeechJobData;
            if (job_data == null)
            {
                return 0;
            }

            int buffer_capacity = job_data.BufferCapacity;
            byte[] buffer = new byte[2 * buffer_capacity];
            AitalkCore.Result result;
            int read_samples;
            do
            {
                result = AitalkCore.GetData(job_id, buffer, buffer_capacity, out read_samples);
                if (result != AitalkCore.Result.Success)
                {
                    break;
                }
                job_data.Output.AddRange(new ArraySegment<byte>(buffer, 0, 2 * read_samples));
            }
            while ((buffer_capacity - 1) <= read_samples);
            if (reason == AitalkCore.EventReason.RawBufferClose)
            {
                job_data.CloseEvent.Set();
            }
            return 0;
        }

        private static int TtsEventCallback(AitalkCore.EventReason reason, int job_id, long tick, string name, IntPtr user_data)
        {
            // phon チャンクを使わないので何もしない。
            return 0;
        }

        public static AitalkParameter Parameter { get; private set; }
        public static string InstallDirectory { get; private set; }
        public static bool IsInitialized { get; private set; } = false;
        public static bool IsLanguageLoaded => CurrentLanguage != null;
        public static string CurrentLanguage { get; private set; }
        public static bool IsVoiceLoaded => CurrentVoice != null;
        public static string CurrentVoice { get; private set; }

        private class KanaJobData
        {
            public int BufferCapacity;
            public List<byte> Output;
            public EventWaitHandle CloseEvent;
        }

        private class SpeechJobData
        {
            public int BufferCapacity;
            public List<byte> Output;
            public EventWaitHandle CloseEvent;
        }

        private const int VoiceSampleRate = 44100;
        private const int TimeoutMilliseconds = 1000;

        [DllImport("Kernel32.dll")]
        private static extern bool SetDllDirectory(string lpPathName);
    }

    /// <summary>
    /// AitalkException がどのカテゴリのエラーかを表す。
    /// Program.cs の catch 節でプロセス終了コードに変換し、API 側で HTTP ステータスにマップする。
    /// </summary>
    public enum AitalkErrorKind
    {
        /// <summary>内部エラー (バグ / DLL 異常 / タイムアウト)。HTTP 500 系にマップ。</summary>
        Internal,
        /// <summary>ユーザー入力起因 (未知の voice_db / voice_name など)。HTTP 400 にマップ。</summary>
        UserInput,
        /// <summary>サーバー設定起因 (認証コード不正、ライセンス期限切れなど)。HTTP 500 にマップしつつメッセージで誘導。</summary>
        ServerConfig,
    }

    public class AitalkException : Exception
    {
        public AitalkErrorKind Kind { get; }

        public AitalkException() : base() { Kind = AitalkErrorKind.Internal; }
        public AitalkException(string message) : base(message) { Kind = AitalkErrorKind.Internal; }
        public AitalkException(string message, AitalkErrorKind kind) : base(message) { Kind = kind; }
        internal AitalkException(string message, AitalkCore.Result result) : base($"{message}({result})")
        {
            Kind = ClassifyResult(result);
        }
        public AitalkException(string message, Exception inner) : base(message, inner) { Kind = AitalkErrorKind.Internal; }

        private static AitalkErrorKind ClassifyResult(AitalkCore.Result r) => r switch
        {
            AitalkCore.Result.PathNotFound => AitalkErrorKind.UserInput,
            AitalkCore.Result.FileNotFound => AitalkErrorKind.UserInput,
            AitalkCore.Result.InvalidArgument => AitalkErrorKind.UserInput,
            AitalkCore.Result.LicenseAbsent => AitalkErrorKind.ServerConfig,
            AitalkCore.Result.LicenseExpired => AitalkErrorKind.ServerConfig,
            AitalkCore.Result.LicenseRejected => AitalkErrorKind.ServerConfig,
            _ => AitalkErrorKind.Internal,
        };
    }
}
