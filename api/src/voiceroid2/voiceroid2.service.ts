import { Injectable, Logger } from '@nestjs/common';
import * as fs from 'fs/promises';
import * as path from 'path';
import * as os from 'os';
import { Voiceroid2Cli } from './voiceroid2-cli';
import { WorkerStatus } from './dto/status.dto';
import { parseLines } from '@/shared/utils/parse-lines';

export interface SynthesizeOptions {
  text: string;
  speaker?: string;
}

export interface TalkOptions {
  text: string;
  speaker?: string;
}

@Injectable()
export class Voiceroid2Service {
  private readonly logger = new Logger(Voiceroid2Service.name);
  // VOICEROID2 は単一インスタンス。リクエストは直列化する。
  private queue: Promise<void> = Promise.resolve();
  private pendingCount = 0;

  constructor(private readonly cli: Voiceroid2Cli) {}

  getStatus(): WorkerStatus {
    return {
      status: this.pendingCount > 0 ? 'busy' : 'idle',
      queue_length: this.pendingCount,
    };
  }

  async listSpeakers(): Promise<string[]> {
    const { stdout, stderr } = await this.cli.exec(['--list-speakers']);
    if (stderr) {
      this.logger.warn(`helper stderr: ${stderr}`);
    }
    return parseLines(stdout);
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
    const args = ['--talk', '--text', options.text];
    if (options.speaker) {
      args.push('--speaker', options.speaker);
    }
    this.logger.log(
      `talk: ${options.speaker ?? '(default)'} "${options.text}"`,
    );
    await this.execAndLogStderr(args);
  }

  private async doSynthesize(options: SynthesizeOptions): Promise<Buffer> {
    const tmpDir = await fs.mkdtemp(path.join(os.tmpdir(), 'voiceroid2-'));
    const outFile = path.join(tmpDir, 'out.wav');
    try {
      const args = ['--save', '--text', options.text, '--out', outFile];
      if (options.speaker) {
        args.push('--speaker', options.speaker);
      }
      this.logger.log(
        `synthesize: ${options.speaker ?? '(default)'} "${options.text}" -> ${outFile}`,
      );
      await this.execAndLogStderr(args);
      return await fs.readFile(outFile);
    } finally {
      await fs.rm(tmpDir, { recursive: true, force: true });
    }
  }

  // execFile はタイムアウト等で reject したときも error.stderr に
  // 子プロセスが書き出した stderr 文字列を保持している (Node のドキュメント参照)。
  // 失敗時の調査ができるように、成功・失敗どちらでも stderr を warn ログに出す。
  private async execAndLogStderr(args: string[]): Promise<void> {
    try {
      const { stderr } = await this.cli.exec(args);
      if (stderr) {
        this.logger.warn(`helper stderr: ${stderr}`);
      }
    } catch (e) {
      const err = e as Error & { stderr?: string; stdout?: string };
      if (err.stderr) {
        this.logger.warn(`helper stderr (failed): ${err.stderr}`);
      }
      throw e;
    }
  }
}
