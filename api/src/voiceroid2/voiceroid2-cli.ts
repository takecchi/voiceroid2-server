import { Injectable } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { execFile } from 'child_process';
import { promisify } from 'util';

const execFileAsync = promisify(execFile);

/**
 * VOICEROID2 のヘルパー .exe を呼び出すラッパー。
 *
 * ヘルパーは /helper にある C# コンソールアプリで、VOICEROID2 同梱の `aitalked.dll` を
 * P/Invoke で直接叩いて音声合成を行う (旧 Codeer.Friendly + WPF UI 自動操作の置き換え)。
 *
 * 主要サブコマンド:
 *  - --list-voice-dbs                : インストール済みボイスライブラリを 1 行ずつ stdout
 *  - --list-speakers --voice-db NAME : ボイスライブラリ内の話者名を 1 行ずつ stdout
 *  - --talk --text "..."             : 既定スピーカーで再生のみ (.NET SoundPlayer 同期再生)
 *  - --save --text "..." --out FILE  : 44.1kHz/16bit/mono の RIFF WAV をファイル出力
 *  - --get-key                       : (初回セットアップ) VoiceroidEditor から認証コードシードを取得
 *
 * 終了コードは helper/Program.cs の規約に従う (Service 側で HTTP ステータスに変換):
 *  - 0 成功 / 1 内部エラー / 2 ユーザー入力エラー / 3 サーバー設定 (認証コード等) エラー
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
