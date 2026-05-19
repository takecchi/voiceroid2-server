# voiceroid2-server プロジェクトガイド

> このファイルは Claude Code 用のプロジェクト固有ルール。
> ユーザーグローバル ~/.claude/rules/ も併用する。

## プロジェクト概要

VOICEROID2 を REST API として呼び出せるようにする。

- `api/` — NestJS REST サーバー。
- `helper/` — VOICEROID2 同梱の **`aitalked.dll` を P/Invoke で直接叩く** C# CLI。
  voiceroid_daemon (https://github.com/Nkyoku/voiceroid_daemon) の Aitalk ラッパーを移植している。
  **VOICEROID2 エディタ (GUI) の起動は不要** (初回 `--get-key` でライセンスキーを抜くときだけ起動が必要)。
- Docker は使わない。Windows ホストで `helper/*.exe` をビルドして API サーバーから直接 spawn する。

## アーキテクチャ概要

```
REST request → NestJS (Voiceroid2Service)
              → spawn helper.exe (one-shot)
                → aitalked.dll Init → LoadLanguage → LoadVoice → TextToKana → KanaToSpeech
                → 標準 RIFF WAV を --out に書き出す or SoundPlayer で再生
              → WAV を読み戻して HTTP レスポンス
```

helper は呼び出しごとに DLL を初期化する one-shot 設計。初期化に 1〜2 秒かかるので、
将来パフォーマンスが必要になったら helper を long-running daemon にして stdin/stdout
で JSON プロトコルを話す形に進化させる。

## 絶対厳守事項

### VOICEROID2 のライセンス / アクティベーション

- VOICEROID2 はマシン固定ライセンス。ローカルでインストール & アクティベーションした
  Windows 環境で動かす想定。
- `aitalked.dll` の認証コードシード (= VoiceroidEditor の `AI.Framework.App` の
  `AppSettings.LicenseKey`) を初回だけ取得する必要がある。手順:
  1. VOICEROID2 エディタを起動する。
  2. `voiceroid2-helper.exe --get-key` を実行する (stdout にシードが出る)。
  3. `.env` の `VOICEROID2_AUTH_CODE` に貼り付ける。
     以後 VOICEROID2 エディタの起動は不要。
- 取得した認証コードは VOICEROID2 がインストールされた**このマシンでしか有効でない**。
- VOICEROID2 本体のアンインストール前にはディアクティベートを忘れない (ユーザー責任)。

### 同時実行制約

- `aitalked.dll` はプロセス内で 1 つしか初期化できない。helper はプロセスごとに DLL を
  init するが、複数 helper を同時に走らせると DLL のグローバル状態 (ライセンスチェック、
  ボイスライブラリのロード状態) が競合する可能性がある。
- API サーバー側で `Voiceroid2Service` がリクエストを直列化している (`queue` フィールド)。
  この直列化は**絶対に外さないこと**。

### 文字エンコーディング / 入力

- helper.exe の stdout は UTF-8 (`Console.OutputEncoding = UTF8`)。NestJS 側も UTF-8 でデコードする。
- 入力テキストは内部で ShiftJIS (CP932) に変換して aitalked.dll へ渡す
  (`AitalkWrapper.UnicodeToShiftJis`)。日本語以外の文字は化ける。
- 話者切替記号 (`>` 等) は不要。話者は `voice_db` と `voice_name` の引数で指定する。

## 検証ルール

DLL 直叩きになったので GUI バインディング揺れの検証は不要になった。代わりに以下を守る:

1. **認証コードが有効か**: `--list-voice-dbs` が空配列でなく返ること。
2. **音声合成の golden path**: `--save --text "テスト" --voice-db <db>` で WAV が出ること。
3. **マスターチューニング**: `--volume / --speed / --pitch / --intonation` が音に反映されること。
4. **失敗ケース**: `--auth-code` が誤っていれば `AitalkException(LicenseRejected)` が出ること。

## DLL 直叩きで詰まりやすいポイント

### ビルドターゲット

- `aitalked.dll` は x86 stdcall (`_AITalkAPI_xxx@N`)。
  helper の csproj は `<PlatformTarget>x86</PlatformTarget>` を**必ず維持**すること。
  AnyCPU でビルドすると `EntryPointNotFoundException` か `BadImageFormatException` で死ぬ。

### DLL 探索パス

- `AitalkWrapper.Initialize` 内で `SetDllDirectory(install_directory)` を呼ぶことで
  `aitalked.dll` の依存 DLL を VOICEROID2 のフォルダから引いている。
  helper.exe を VOICEROID2 ディレクトリ以外で動かす場合もこの呼び出しが必須。

### 言語ライブラリのロード

- `AITalkAPI_LangLoad` はカレントディレクトリが install_dir 以外だと失敗する。
  `AitalkWrapper.LoadLanguage` で一時的に `Directory.SetCurrentDirectory` を入れているので
  そこを削らないこと。

### 権限の揃え方

- 既定では helper は `asInvoker` (管理者不要)。
- ただし `--get-key` (VoiceroidEditor に DLL inject) を使うときは Codeer.Friendly で他プロセスに
  アタッチするので、**helper と VOICEROID2 エディタが同権限である必要がある**。
  VOICEROID2 を管理者で起動している場合は helper も管理者で起動すること
  (または VOICEROID2 を非管理者で起動する)。「権限を揃える」が原則。

### 出力 WAV のフォーマット

- 44.1kHz / 16bit / mono。標準 RIFF。
- voiceroid_daemon の元実装にあった `phon` チャンク (TTS イベントの JSON) は省いている。
  必要になったら `AitalkWrapper.KanaToSpeech` に書き戻す。

## API サーバー側ルール

- DTO は `class-validator` + `@nestjs/swagger` の `@ApiProperty` を必ず付ける。
- Optional プロパティには `@NullToUndefined()` を付け、`null` を `undefined` に正規化する
  (`@takecchi/class-transformer` を使う前提)。
- 例外処理は `GlobalExceptionFilter` に集約。ハンドラ側で try/catch しない。
- 環境変数:
  - `PORT` — リッスンポート (default: 8181)
  - `VOICEROID2_HELPER_PATH` — helper.exe のフルパス
  - `VOICEROID2_TIMEOUT_MS` — helper 呼び出しタイムアウト (default: 120000)
  - `VOICEROID2_INSTALL_DIR` — VOICEROID2 インストールディレクトリ
    (default: `C:\Program Files (x86)\AHS\VOICEROID2`)
  - `VOICEROID2_AUTH_CODE` — `aitalked.dll` の認証コードシード (必須)
  - `VOICEROID2_DEFAULT_VOICE_DB` — リクエスト省略時のボイスライブラリ (任意)
  - `VOICEROID2_DEFAULT_VOICE_NAME` — リクエスト省略時の話者名 (任意)
  - `LOG_LEVEL` — winston ログレベル (default: info)

## 運用方針

- helper.exe を Visual Studio / `dotnet build -c Release` で先にビルドし、生成された
  `voiceroid2-helper.exe` のパスを `VOICEROID2_HELPER_PATH` で API に渡す。
- 初回セットアップ:
  1. VOICEROID2 エディタを起動。
  2. `voiceroid2-helper.exe --get-key` で認証コードシード取得。
  3. `.env` の `VOICEROID2_AUTH_CODE` に貼り付け。
  4. VOICEROID2 エディタは閉じてよい。
- API サーバー: `cd api && npm ci && npm run build && npm run start:prod`
- Swagger: http://localhost:8181/api (NODE_ENV != production 時)
