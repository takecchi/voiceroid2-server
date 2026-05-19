# voiceroid2-server

Windows 用 VOICEROID2 を REST API として利用するためのプロジェクト。

VOICEROID2 の GUI を Codeer.Friendly 経由で裏側から操作し、WAV を返す HTTP API として
利用できるようにする。

- `/voiceroid2/speech` (POST) → 音声合成して `audio/wav` を返す
- `/voiceroid2/talk` (POST) → スピーカーで再生のみ
- `/voiceroid2/speakers` (GET) → 利用可能な話者一覧
- `/voiceroid2/status` (GET) → ワーカーキューの状態
- `/voiceroid2/health` (GET)

## 前提条件

- **Windows 10/11** (VOICEROID2 が動く環境)
- VOICEROID2 がインストール & アクティベーション済み
- .NET Framework 4.8 SDK / Visual Studio 2022 (helper.exe ビルド用)
- Node.js 20+ / npm

Docker は使わない。

## 構成

```
voiceroid2-server/
├── api/      NestJS REST サーバー
└── helper/   VOICEROID2 操作用の C# CLI (.NET Framework 4.8, x86)
```

API → helper.exe を spawn → helper が VOICEROID2 の WPF UI を Codeer.Friendly で操作。

## セットアップ

### 1. 依存をインストール

```powershell
npm ci
```

ルートで `npm ci` を一度実行すれば、api ワークスペースの依存も合わせて入る。

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
> VOICEROID2 が x86 プロセスなので helper も **x86** でビルドする必要があるが、
> `.csproj` で `PlatformTarget=x86` 固定済みなので追加指定は不要。

### 3. API サーバーの .env を用意

```powershell
copy api\.env.example api\.env
```

`.env` 例:

```
PORT=8181
VOICEROID2_HELPER_PATH=C:\path\to\voiceroid2-server\helper\bin\Release\net481\voiceroid2-helper.exe
VOICEROID2_TIMEOUT_MS=120000
LOG_LEVEL=info
```

### 4. 起動

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

# 話者一覧
curl http://localhost:8181/voiceroid2/speakers

# WAV を合成して保存
curl -X POST http://localhost:8181/voiceroid2/speech `
  -H "Content-Type: application/json" `
  -d '{\"text\": \"こんにちは\", \"speaker\": \"結月ゆかり\"}' `
  --output test.wav

# スピーカーで再生だけ
curl -X POST http://localhost:8181/voiceroid2/talk `
  -H "Content-Type: application/json" `
  -d '{\"text\": \"てすと\"}'
```

## API エンドポイント

| メソッド | パス                   | 説明                                  |
| -------- | ---------------------- | ------------------------------------- |
| GET      | `/voiceroid2/health`   | ヘルスチェック                        |
| GET      | `/voiceroid2/status`   | キュー状態 (idle/busy + queue_length) |
| GET      | `/voiceroid2/speakers` | 話者名一覧                            |
| POST     | `/voiceroid2/talk`     | スピーカーで再生のみ                  |
| POST     | `/voiceroid2/speech`   | WAV を生成して返す                    |

### POST `/voiceroid2/speech`

| パラメータ | 型     | 必須 | 説明                      |
| ---------- | ------ | ---- | ------------------------- |
| `text`     | string | ✓    | 読み上げるテキスト        |
| `speaker`  | string |      | 話者名 (例: `結月ゆかり`) |

レスポンス: `Content-Type: audio/wav` の WAV バイナリ。

### POST `/voiceroid2/talk`

| パラメータ | 型     | 必須 | 説明               |
| ---------- | ------ | ---- | ------------------ |
| `text`     | string | ✓    | 読み上げるテキスト |
| `speaker`  | string |      | 話者名             |

レスポンス: JSON `{ success: true, message: "Talked: ..." }`

## 制約

- **VOICEROID2 は同時 1 インスタンス**。API サーバーで直列化しているので並列リクエストは
  内部でキューイングされる (`/voiceroid2/status` で確認可能)。
- helper は Codeer.Friendly + RM.Friendly.WPFStandardControls で UI 自動操作している。
  VOICEROID2 のバージョンで Binding 名が変わるとビルドは通っても実行時に失敗する。
  `helper/Voiceroid2Driver.cs` の `SaveCommandCandidates` を調整するか、`Snoop` で確認してから直す。
- 「音声保存」フロー (注意ダイアログ → SaveFileDialog → 完了ダイアログ) のラベルは
  **日本語前提** で実装している。英語版 VOICEROID2 では動かない。

## ファイル構成

| パス                          | 用途                                                                   |
| ----------------------------- | ---------------------------------------------------------------------- |
| `api/`                        | NestJS REST サーバー                                                   |
| `api/src/voiceroid2/`         | VOICEROID2 モジュール (controller / service / cli wrapper / DTO)       |
| `api/src/shared/`             | logging / utils / decorators                                           |
| `helper/`                     | VOICEROID2 操作用 C# CLI                                               |
| `helper/Program.cs`           | エントリポイント / CLI 引数パース                                      |
| `helper/Voiceroid2Locator.cs` | プロセスの探索と起動 (env / レジストリ / 既定パスの多段フォールバック) |
| `helper/Voiceroid2Driver.cs`  | 編集ビューのコントロール発見と高レベル操作                             |
| `helper/SaveAudioFlow.cs`     | 音声保存ダイアログのモーダル自動操作                                   |
| `helper/NativeWindows.cs`     | Win32 P/Invoke (#32770 ダイアログ操作用)                               |
| `CLAUDE.md`                   | Claude Code 向けプロジェクトルール                                     |

## 参考

- [SimpleVoiceroid2Proxy](https://github.com/SlashNephy/SimpleVoiceroid2Proxy) — VOICEROID2 を Codeer.Friendly で自動操作する先行実装
- [Voiceroid2Proxy](https://github.com/kanosaki/Voiceroid2Proxy) — さらに元ネタ
- [Codeer.Friendly](https://github.com/Codeer-Software/Friendly.Windows.Grasp)
