import { Injectable } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { execFile } from 'child_process';
import { promisify } from 'util';

const execFileAsync = promisify(execFile);

/**
 * VOICEROID2 のヘルパー .exe を呼び出すラッパー。
 *
 * ヘルパーは /helper にある C# コンソールアプリ (Codeer.Friendly ベース) で、
 * VOICEROID2 の WPF UI を制御して以下を行う:
 *  - --list-speakers           : 利用可能な話者を1行ずつ stdout に出力
 *  - --talk --text "..."       : テキストを再生 (スピーカー出力のみ)
 *  - --save --text "..." --out FILE
 *                              : VOICEROID2 の「音声保存」フローでWAVを書き出す
 *
 * 共通オプション:
 *  - --speaker NAME            : 先頭で話者切替する (例: 結月ゆかり)
 */
@Injectable()
export class Voiceroid2Cli {
  private readonly helperPath: string;
  private readonly defaultTimeoutMs: number;

  constructor(configService: ConfigService) {
    // 例: C:\\voiceroid2-server\\helper\\voiceroid2-helper.exe
    this.helperPath = configService.get<string>(
      'VOICEROID2_HELPER_PATH',
      './helper/voiceroid2-helper.exe',
    );
    this.defaultTimeoutMs = Number(
      configService.get<string>('VOICEROID2_TIMEOUT_MS', '120000'),
    );
  }

  async exec(
    args: string[],
    timeout = this.defaultTimeoutMs,
  ): Promise<{ stdout: string; stderr: string }> {
    return execFileAsync(this.helperPath, args, {
      timeout,
      // 出力に日本語が含まれるので UTF-8 で受ける
      // (helper 側で Console.OutputEncoding = UTF8 にする)
      encoding: 'utf8',
      maxBuffer: 50 * 1024 * 1024,
    });
  }
}
