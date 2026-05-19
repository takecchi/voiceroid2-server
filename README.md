# voiceroid2-server

Windows 用 VOICEROID2 を REST API として利用するためのプロジェクト。

VOICEROID2 同梱の `aitalked.dll` を P/Invoke で直接叩いて音声合成し、WAV を返す HTTP API として
利用できるようにする。**VOICEROID2 エディタ (GUI) の起動は不要** (初回 1 回だけ認証コードシードを
取得するときに必要)。

- `/voiceroid2/voice-dbs` (GET) → インストール済みボイスライブラリ一覧
- `/voiceroid2/voice-dbs/:voice_db/speakers` (GET) → ボイスライブラリ内の話者名一覧
- `/voiceroid2/speech` (POST) → 音声合成して `audio/wav` を返す
- `/voiceroid2/talk` (POST) → スピーカーで再生のみ
- `/voiceroid2/status` (GET) → ワーカーキューの状態
- `/voiceroid2/health` (GET)

合成エンジンの移植元: [voiceroid_daemon](https://github.com/Nkyoku/voiceroid_daemon)

## 前提条件

- **Windows 10/11** (VOICEROID2 が動く環境)
- VOICEROID2 がインストール & アクティベーション済み
- .NET Framework 4.8 SDK / Visual Studio 2022 (helper.exe ビルド用、`dotnet` CLI で可)
- Node.js 20+ / npm

Docker は使わない。VOICEROID2 はマシン固定ライセンスなので、ローカルでアクティベート済みの
Windows ホストで動かす。

## 構成

```
voiceroid2-server/
├── api/      NestJS REST サーバー (TypeScript)
└── helper/   aitalked.dll を直接叩く C# CLI (.NET Framework 4.8, x86)
```

```
REST request → NestJS (Voiceroid2Service)
              → spawn helper.exe (one-shot)
                → aitalked.dll Init → LoadLanguage → LoadVoice → TextToKana → KanaToSpeech
                → 標準 RIFF WAV を --out に書き出す or SoundPlayer で再生
              → WAV を読み戻して HTTP レスポンス
```

helper はリクエストごとに DLL を初期化する one-shot 設計 (初期化に 1〜2 秒)。
複数リクエストはサーバー側でキューイングして直列化する。

## セットアップ

### 1. 依存をインストール (リポジトリルートで)

```powershell
npm ci
```

> ⚠ ワークスペース構成なので `api/` の中で `npm ci` を実行すると husky の prepare が
> 失敗する。必ずリポジトリルートから叩くこと。

### 2. ビルド

```powershell
# api + helper をまとめて
npm run build

# 個別に
npm run build:api      # NestJS の dist/ を生成
npm run build:helper   # helper\bin\Release\net481\voiceroid2-helper.exe を生成
```

> `build:helper` は内部で `dotnet build helper/Voiceroid2Helper.csproj -c Release` を呼ぶ。
> .NET SDK が入っていない場合は MSBuild 直叩きでも可:
> `msbuild helper\Voiceroid2Helper.csproj -property:Configuration=Release -restore`
>
> `aitalked.dll` は **x86 stdcall** なので helper も x86 でビルドする必要があるが、
> `.csproj` で `PlatformTarget=x86` 固定済みなので追加指定は不要。

### 3. 認証コードシードを取得 (初回のみ)

`aitalked.dll` は VoiceroidEditor の内部設定値 `AppSettings.LicenseKey` を認証コードシードとして
要求する。これは初回だけエディタから抜き出して `.env` に保存する。

```powershell
# VOICEROID2 エディタを (非管理者で) 起動した状態で:
.\helper\bin\Release\net481\voiceroid2-helper.exe --get-key
# → stdout に "ORXJC6AI..." のような文字列が出る
```

> `--get-key` は Codeer.Friendly で VoiceroidEditor.exe にアタッチして取得する仕組み。
> **helper と VoiceroidEditor の権限 (UAC レベル) が一致していないとアタッチに失敗する**
> ので、どちらも非管理者で起動するのが無難。
>
> 取得した認証コードは **この VOICEROID2 がインストールされたマシンでのみ有効**。
> 別マシンに転用してもライセンスチェックで弾かれる。

取得した値を後述の `VOICEROID2_AUTH_CODE` に貼り付ければ、以降 VOICEROID2 エディタを
起動する必要はない。

### 4. API サーバーの .env を用意

```powershell
copy api\.env.example api\.env
```

`.env` の主なキー:

```
PORT=8181
LOG_LEVEL=info
# 任意。設定するとリクエストに X-API-Key ヘッダー必須 (一致しないと 401)
API_KEY=

# helper.exe のフルパス
VOICEROID2_HELPER_PATH=D:\path\to\voiceroid2-server\helper\bin\Release\net481\voiceroid2-helper.exe
VOICEROID2_TIMEOUT_MS=120000

# VOICEROID2 のインストールディレクトリ (aitalked.dll / Voice/ / Lang/ / aitalk.lic がある場所)
VOICEROID2_INSTALL_DIR=C:\Program Files (x86)\AHS\VOICEROID2

# --get-key で取得した値 (必須)
VOICEROID2_AUTH_CODE=

# 任意。voice_db / voice_name を省略した時のデフォルト
VOICEROID2_DEFAULT_VOICE_DB=
VOICEROID2_DEFAULT_VOICE_NAME=
```

#### API キー (任意)

`API_KEY` 環境変数を設定すると、`/voiceroid2/health` 以外のエンドポイントは
`X-API-Key` ヘッダーで認証する。未設定なら認証無効。

```powershell
curl -H "X-API-Key: $env:API_KEY" http://localhost:8181/voiceroid2/voice-dbs
```

Swagger UI からは右上の Authorize ボタンで設定できる。

### 5. 起動

```powershell
npm start
```

Swagger: <http://localhost:8181/api>

### 主要なコマンド一覧

| コマンド               | 内容                                  |
| ---------------------- | ------------------------------------- |
| `npm run build`        | api + helper をビルド                 |
| `npm run build:api`    | NestJS だけビルド                     |
| `npm run build:helper` | helper (.NET) だけビルド              |
| `npm run clean:helper` | helper のビルド成果物を削除           |
| `npm start`            | NestJS サーバーを production モードで |
| `npm run tsc`          | api の型チェックのみ                  |
| `npm run lint`         | api の eslint --fix                   |
| `npm run format`       | prettier 全体                         |

## 動作確認

```powershell
# ヘルスチェック
curl http://localhost:8181/voiceroid2/health

# ボイスライブラリ一覧
curl http://localhost:8181/voiceroid2/voice-dbs

# ボイスライブラリ内の話者一覧
curl http://localhost:8181/voiceroid2/voice-dbs/tsuina_44/speakers

# WAV を合成して保存
curl -X POST http://localhost:8181/voiceroid2/speech `
  -H "Content-Type: application/json" `
  -d '{\"text\": \"こんにちは\", \"voice_db\": \"tsuina_44\"}' `
  --output test.wav

# スピーカーで再生だけ
curl -X POST http://localhost:8181/voiceroid2/talk `
  -H "Content-Type: application/json" `
  -d '{\"text\": \"てすと\", \"voice_db\": \"tsuina_44\"}'
```

## API エンドポイント

| メソッド | パス                                       | 説明                                  |
| -------- | ------------------------------------------ | ------------------------------------- |
| GET      | `/voiceroid2/health`                       | ヘルスチェック                        |
| GET      | `/voiceroid2/status`                       | キュー状態 (idle/busy + queue_length) |
| GET      | `/voiceroid2/voice-dbs`                    | ボイスライブラリ名一覧                |
| GET      | `/voiceroid2/voice-dbs/:voice_db/speakers` | 指定ボイスライブラリ内の話者名一覧    |
| POST     | `/voiceroid2/talk`                         | スピーカーで再生のみ                  |
| POST     | `/voiceroid2/speech`                       | WAV を生成して返す                    |

### POST `/voiceroid2/speech` / `/voiceroid2/talk`

両者ともリクエストボディは同じ:

| パラメータ   | 型     | 必須                                        | 説明                                                    |
| ------------ | ------ | ------------------------------------------- | ------------------------------------------------------- |
| `text`       | string | ✓                                           | 読み上げるテキスト                                      |
| `voice_db`   | string | 環境変数 `VOICEROID2_DEFAULT_VOICE_DB` 次第 | ボイスライブラリ名 (例: `tsuina_44`, `yukari_44`)       |
| `voice_name` | string |                                             | 話者名 (例: `結月ゆかり`)。省略時は voice_db の先頭話者 |
| `volume`     | number |                                             | マスター音量 0.0-5.0 (default 1.0)                      |
| `speed`      | number |                                             | 話速 0.5-4.0 (default 1.0)                              |
| `pitch`      | number |                                             | 高さ 0.5-2.0 (default 1.0)                              |
| `intonation` | number |                                             | 抑揚 0.0-2.0 (default 1.0)                              |

- `/speech` のレスポンス: `Content-Type: audio/wav` (44.1kHz / 16bit / mono)
- `/talk` のレスポンス: JSON `{ success: true, message: "Talked: ..." }`

入力テキストは内部で ShiftJIS (CP932) に変換して `aitalked.dll` に渡すので、
日本語以外の文字は化ける。

### エラーレスポンス

| ケース                                           | ステータス | 例                                                                                      |
| ------------------------------------------------ | ---------- | --------------------------------------------------------------------------------------- |
| バリデーションエラー (`text` 空など)             | 400        | `{"statusCode":400,"message":["text should not be empty"]}`                             |
| `voice_db` 未指定 & デフォルト env も無し        | 400        | `voice_db is required (specify in request body or VOICEROID2_DEFAULT_VOICE_DB env)`     |
| 未知の `voice_db` / `voice_name`                 | 400        | helper の `AitalkException(PathNotFound)` / `話者'xxx'は存在しません。` を転送          |
| `VOICEROID2_AUTH_CODE` 不正 / ライセンス期限切れ | 503        | `helper server-config error: ... (VOICEROID2_AUTH_CODE / ライセンスを確認してください)` |
| その他 (DLL 異常、タイムアウト等)                | 500        | `Internal Server Error` (詳細はサーバーログ)                                            |

> helper は終了コード 1 (内部) / 2 (ユーザー入力) / 3 (サーバー設定) で API に状態を伝え、
> API がそれを HTTP ステータスにマップする。詳細は CLAUDE.md の「helper の終了コード規約」。

## 制約

- **VOICEROID2 (aitalked.dll) は同時 1 インスタンス**。API サーバー内のキューで直列化しているので、
  並列リクエストは内部で順番に処理される (`/voiceroid2/status` で `queue_length` を見れる)。
- helper は **x86 stdcall** な `aitalked.dll` を読むため、`.csproj` の `PlatformTarget=x86` を
  外さない。AnyCPU でビルドすると `EntryPointNotFoundException` か `BadImageFormatException` で死ぬ。
- `--get-key` を使う初回セットアップ時のみ Codeer.Friendly で VoiceroidEditor にアタッチするので、
  **helper と VoiceroidEditor の権限 (UAC レベル) を揃える** こと。本番運用 (DLL 直叩き) では不要。
- 入力テキストは ShiftJIS にエンコードして渡すので、日本語以外の文字は化ける。

## ファイル構成

| パス                               | 用途                                                                       |
| ---------------------------------- | -------------------------------------------------------------------------- |
| `api/`                             | NestJS REST サーバー                                                       |
| `api/src/voiceroid2/`              | VOICEROID2 モジュール (controller / service / cli wrapper / DTO)           |
| `api/src/shared/`                  | logging / utils / decorators / guards                                      |
| `helper/`                          | aitalked.dll を直接叩く C# CLI                                             |
| `helper/Program.cs`                | エントリポイント / CLI 引数パース / 終了コード規約                         |
| `helper/Aitalk/AitalkCore.cs`      | aitalked.dll の P/Invoke 定義 (struct / enum / DllImport)                  |
| `helper/Aitalk/AitalkParameter.cs` | TtsParam / SpeakerParam の C# ラッパー                                     |
| `helper/Aitalk/AitalkWrapper.cs`   | Init / LoadLanguage / LoadVoice / TextToKana / KanaToSpeech の高レベル API |
| `helper/KeyExtractor.cs`           | `--get-key`: VoiceroidEditor から `AppSettings.LicenseKey` を抜き出す      |
| `CLAUDE.md`                        | Claude Code 向けプロジェクトルール                                         |

## 参考

- [voiceroid_daemon](https://github.com/Nkyoku/voiceroid_daemon) — aitalked.dll 直叩き手法の元実装
- [Codeer.Friendly](https://github.com/Codeer-Software/Friendly.Windows.Grasp) — `--get-key` で
  VoiceroidEditor にアタッチするためだけに使用
