import {
  BadRequestException,
  Injectable,
  Logger,
  ServiceUnavailableException,
} from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import * as fs from 'fs/promises';
import * as path from 'path';
import * as os from 'os';
import { Voiceroid2Cli } from './voiceroid2-cli';
import { WorkerStatus } from './dto/status.dto';
import { parseLines } from '@/shared/utils/parse-lines';

// helper.exe の終了コード規約 (helper/Program.cs と合わせる):
//   1 = 内部エラー / 2 = ユーザー入力エラー / 3 = サーバー設定エラー
const HELPER_EXIT_USER_INPUT = 2;
const HELPER_EXIT_SERVER_CONFIG = 3;

interface VoiceTuning {
  volume?: number;
  speed?: number;
  pitch?: number;
  intonation?: number;
}

export interface SynthesizeOptions extends VoiceTuning {
  text: string;
  voice_db?: string;
  voice_name?: string;
}

export interface TalkOptions extends VoiceTuning {
  text: string;
  voice_db?: string;
  voice_name?: string;
}

// VOICEROID2 のマスター効果は前回値が残るため、未指定時も明示的に 1.0 を送って初期化する。
// helper の DLL 直叩きでも同じ理由 (Initialize → LoadVoice 後に GUI の前回値が初期パラメータに入る)。
const TUNING_DEFAULT = 1.0;

function buildTuningArgs(options: VoiceTuning): string[] {
  return [
    '--volume',
    (options.volume ?? TUNING_DEFAULT).toString(),
    '--speed',
    (options.speed ?? TUNING_DEFAULT).toString(),
    '--pitch',
    (options.pitch ?? TUNING_DEFAULT).toString(),
    '--intonation',
    (options.intonation ?? TUNING_DEFAULT).toString(),
  ];
}

@Injectable()
export class Voiceroid2Service {
  private readonly logger = new Logger(Voiceroid2Service.name);
  // helper.exe (DLL 直叩き) を spawn ごとに DLL を初期化する。同じプロセス内では
  // aitalked.dll は 1 つしか初期化できないので、複数呼び出しは直列化する。
  // CLAUDE.md の同時実行制約は据え置き。
  private queue: Promise<void> = Promise.resolve();
  private pendingCount = 0;

  private readonly installDir: string;
  private readonly authCode: string;
  private readonly defaultVoiceDb: string | undefined;
  private readonly defaultVoiceName: string | undefined;

  constructor(
    private readonly cli: Voiceroid2Cli,
    configService: ConfigService,
  ) {
    this.installDir = configService.get<string>(
      'VOICEROID2_INSTALL_DIR',
      'C:\\Program Files (x86)\\AHS\\VOICEROID2',
    );
    this.authCode = configService.get<string>('VOICEROID2_AUTH_CODE', '');
    this.defaultVoiceDb = configService.get<string>(
      'VOICEROID2_DEFAULT_VOICE_DB',
    );
    this.defaultVoiceName = configService.get<string>(
      'VOICEROID2_DEFAULT_VOICE_NAME',
    );
    if (!this.authCode) {
      this.logger.warn(
        'VOICEROID2_AUTH_CODE が設定されていません。helper の --get-key で取得して .env に設定してください。',
      );
    }
  }

  getStatus(): WorkerStatus {
    return {
      status: this.pendingCount > 0 ? 'busy' : 'idle',
      queue_length: this.pendingCount,
    };
  }

  async listVoiceDbs(): Promise<string[]> {
    // DLL 初期化不要なので queue 外で即時実行。
    const { stdout, stderr } = await this.cli.exec([
      '--list-voice-dbs',
      '--install-dir',
      this.installDir,
    ]);
    if (stderr) {
      this.logger.warn(`helper stderr: ${stderr}`);
    }
    return parseLines(stdout);
  }

  async listSpeakers(voice_db: string): Promise<string[]> {
    // DLL を初期化するので queue を経由させる。
    return this.enqueue(async () => {
      const { stdout, stderr } = await this.cli.exec([
        '--list-speakers',
        ...this.commonInitArgs(),
        '--voice-db',
        voice_db,
      ]);
      if (stderr) {
        this.logger.warn(`helper stderr: ${stderr}`);
      }
      return parseLines(stdout);
    });
  }

  async talk(options: TalkOptions): Promise<void> {
    return this.enqueue(() => this.doTalk(options));
  }

  async synthesize(options: SynthesizeOptions): Promise<Buffer> {
    return this.enqueue(() => this.doSynthesize(options));
  }

  private enqueue<T>(fn: () => Promise<T>): Promise<T> {
    this.pendingCount++;
    return new Promise<T>((resolve, reject) => {
      this.queue = this.queue.then(() =>
        fn()
          .then(resolve, reject)
          .finally(() => this.pendingCount--),
      );
    });
  }

  private async doTalk(options: TalkOptions): Promise<void> {
    const args = [
      '--talk',
      '--text',
      options.text,
      ...this.commonInitArgs(),
      ...this.voiceArgs(options),
      ...buildTuningArgs(options),
    ];
    this.logger.log(
      `talk: ${options.voice_db ?? this.defaultVoiceDb ?? '(no voice_db)'}/${options.voice_name ?? this.defaultVoiceName ?? '(first speaker)'} "${options.text}"`,
    );
    await this.execAndLogStderr(args);
  }

  private async doSynthesize(options: SynthesizeOptions): Promise<Buffer> {
    const tmpDir = await fs.mkdtemp(path.join(os.tmpdir(), 'voiceroid2-'));
    const outFile = path.join(tmpDir, 'out.wav');
    try {
      const args = [
        '--save',
        '--text',
        options.text,
        '--out',
        outFile,
        ...this.commonInitArgs(),
        ...this.voiceArgs(options),
        ...buildTuningArgs(options),
      ];
      this.logger.log(
        `synthesize: ${options.voice_db ?? this.defaultVoiceDb ?? '(no voice_db)'}/${options.voice_name ?? this.defaultVoiceName ?? '(first speaker)'} "${options.text}" -> ${outFile}`,
      );
      await this.execAndLogStderr(args);
      return await fs.readFile(outFile);
    } finally {
      await fs.rm(tmpDir, { recursive: true, force: true });
    }
  }

  // すべての DLL 直叩きコマンドで共通の引数。voice_db を指定するコマンドはこれに加えて --voice-db / --voice-name を追加する。
  private commonInitArgs(): string[] {
    return ['--install-dir', this.installDir, '--auth-code', this.authCode];
  }

  private voiceArgs(options: {
    voice_db?: string;
    voice_name?: string;
  }): string[] {
    const voiceDb = options.voice_db ?? this.defaultVoiceDb;
    if (!voiceDb) {
      throw new BadRequestException(
        'voice_db is required (specify in request body or VOICEROID2_DEFAULT_VOICE_DB env)',
      );
    }
    const args = ['--voice-db', voiceDb];
    const voiceName = options.voice_name ?? this.defaultVoiceName;
    if (voiceName) {
      args.push('--voice-name', voiceName);
    }
    return args;
  }

  // execFile はタイムアウト等で reject したときも error.stderr に
  // 子プロセスが書き出した stderr 文字列を保持している (Node のドキュメント参照)。
  // 失敗時の調査ができるように、成功・失敗どちらでも stderr を warn ログに出す。
  // また helper の終了コード規約 (1/2/3) を HTTP ステータスに反映する。
  private async execAndLogStderr(args: string[]): Promise<void> {
    try {
      const { stderr } = await this.cli.exec(args);
      if (stderr) {
        this.logger.warn(`helper stderr: ${stderr}`);
      }
    } catch (e) {
      const err = e as Error & {
        stderr?: string;
        stdout?: string;
        code?: number | string;
      };
      if (err.stderr) {
        this.logger.warn(`helper stderr (failed): ${err.stderr}`);
      }
      throw this.mapHelperError(err);
    }
  }

  // helper.exe の exit code を HTTP 例外に変換する。stderr の最後の "error" 行を
  // メッセージとして拾えれば、ユーザーに返るレスポンスがそこそこ親切になる。
  private mapHelperError(
    err: Error & { stderr?: string; code?: number | string },
  ): Error {
    if (typeof err.code !== 'number') {
      // ETIMEDOUT / 起動失敗等。コードが string で来る — 内部扱い。
      return err;
    }
    const detail = extractHelperErrorLine(err.stderr) ?? err.message;
    if (err.code === HELPER_EXIT_USER_INPUT) {
      return new BadRequestException(detail);
    }
    if (err.code === HELPER_EXIT_SERVER_CONFIG) {
      // ServiceUnavailable (503) を使う理由:
      //   GlobalExceptionFilter は InternalServerErrorException のメッセージを隠蔽するが
      //   (情報リーク抑止)、ServerConfig エラーは「VOICEROID2_AUTH_CODE を直して再起動」
      //   というアクションを運用者に伝える必要があるためメッセージを温存したい。
      //   503 は "サーバー側の都合で一時的に処理できない" 意味で語感的にも合う。
      return new ServiceUnavailableException(
        `helper server-config error: ${detail} (VOICEROID2_AUTH_CODE / ライセンスを確認してください)`,
      );
    }
    return err;
  }
}

// helper の LogWriter.Error 出力 ("HH:mm:ss.fff error LABEL :: ExceptionType: message")
// から末尾の有意なメッセージだけを抜き出す。なければ undefined を返し、呼び出し元で
// fallback メッセージを使う。
function extractHelperErrorLine(stderr?: string): string | undefined {
  if (!stderr) return undefined;
  const errorLine = stderr
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter((l) => / error /.test(l))
    .pop();
  if (!errorLine) return undefined;
  const idx = errorLine.indexOf(' :: ');
  return idx >= 0 ? errorLine.slice(idx + 4) : errorLine;
}
