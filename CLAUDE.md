# voiceroid2-server プロジェクトガイド

> このファイルは Claude Code 用のプロジェクト固有ルール。
> ユーザーグローバル ~/.claude/rules/ も併用する。

## プロジェクト概要

VOICEROID2 (Windows GUI アプリ) を REST API として呼び出せるようにする。

- `api/` — NestJS REST サーバー。
- `helper/` — VOICEROID2 を Codeer.Friendly + WPF で操作する C# CLI。
  責務ごとに Locator / Driver / SaveAudioFlow / NativeWindows でクラス分割している。
- Docker は使わない。Windows ホストで `helper/*.exe` をビルドして API サーバーから直接 spawn する。

## 絶対厳守事項

### VOICEROID2 のライセンス / アクティベーション

- VOICEROID2 はマシン固定ライセンス。ローカルでインストール & アクティベーションした
  Windows 環境で動かす想定。アクティベーション情報は VOICEROID2 側が管理しているので、
  本プロジェクトのファイルを消しても問題ない。
- VOICEROID2 本体のアンインストール前にはディアクティベートを忘れない (ユーザー責任)。

### 同時実行制約

- **VOICEROID2 は 1 インスタンスしか起動できない。**
  API サーバー側で `Voiceroid2Service` がリクエストを直列化している (`queue` フィールド)。
  この直列化を絶対に外さないこと。
- helper.exe は呼び出しごとに既存の VOICEROID2 プロセスへアタッチする。
  もし VOICEROID2 が起動していなければ helper.exe が起動する。

## 検証ルール

UI 自動操作のバインディング名はバージョンで揺れる。変更時は以下の手順を守る:

1. **コントロール確認:** 現状の `--talk` が動くことを確認する (再生だけは安定しやすい)。
2. **実験実施:** バインディング候補を追加 → `--save` を試す。
3. **コントロール再確認:** `--talk` が壊れていないこと。
4. **片付け:** 失敗実験で残ったテストファイルを消す。

## VOICEROID2 自動化の既知ハマりどころ

### WPF Binding 名のバージョン差異

- `helper/Voiceroid2Driver.cs` の `SaveCommandCandidates` 配列に音声保存ボタンの
  候補を列挙している。ヒットしない場合は `Snoop`/`UI Spy` で実機を見て追加する。
- `PlayCommand` / `StopCommand` / `MoveToBeginningCommand` が見つからない場合は
  `RequireBinding` が即 throw する。物理 index フォールバックは意図的に持っていない
  (壊れたら明示的に直す方針)。

### Save Voice ダイアログのフロー

VOICEROID2 の「音声保存」は以下のモーダルが連続する:

1. (場合により) 「テキストが分割されました」等の注意ダイアログ → OK を押す。
2. SaveFileDialog (Win32 #32770) → ファイル名を埋めて 保存 を押す。
3. (場合により) 「保存しました」完了ダイアログ → OK を押す。

`helper/SaveAudioFlow.cs` が別スレッドで `EnumThreadWindows` を回してこれらを掴んで自動操作している。
タイトル / ボタンラベル ("保存", "OK", "VOICEROID", "情報" 等) が日本語前提なので
英語版 VOICEROID2 では動かない (必要なら定数化)。

### 権限の揃え方

- 既定では helper は `asInvoker` (管理者不要)。Codeer.Friendly で他プロセスにアタッチ
  するには helper と VOICEROID2 が同権限である必要があるので、VOICEROID2 を
  管理者で起動している場合は `app.manifest` の `level` を `requireAdministrator`
  に変える (または VOICEROID2 を非管理者で起動する)。「権限を揃える」が原則。

### 文字エンコーディング

- helper.exe の stdout は UTF-8 (`Console.OutputEncoding = UTF8`)。NestJS 側も UTF-8 でデコードする。
- 話者切替記号は **半角 `>`** (ASCII 0x3E)。SimpleVoiceroid2Proxy の README に
  全角 `＞` (U+FF1E, Shift-JIS で 0x81 0x84) と書かれているが、実機検証では
  半角でないと話者切替が効かない (2026-05 確認)。
  VOICEROID2 設定で記号は変更可能だが、既定値は半角の前提でコードを書く。

## API サーバー側ルール

- DTO は `class-validator` + `@nestjs/swagger` の `@ApiProperty` を必ず付ける。
- Optional プロパティには `@NullToUndefined()` を付け、`null` を `undefined` に正規化する。
  (`@takecchi/class-transformer` を使う前提)
- 例外処理は `GlobalExceptionFilter` に集約。ハンドラ側で try/catch しない。
- 環境変数:
  - `PORT` — リッスンポート (default: 8181)
  - `VOICEROID2_HELPER_PATH` — helper.exe のフルパス
  - `VOICEROID2_TIMEOUT_MS` — helper 呼び出しタイムアウト (default: 120000)
  - `LOG_LEVEL` — winston ログレベル (default: info)

## 運用方針

- helper.exe を Visual Studio / `dotnet build` で先にビルドし、生成された `voiceroid2-helper.exe`
  のパスを `VOICEROID2_HELPER_PATH` で API に渡す。
- API サーバー: `cd api && npm ci && npm run build && npm run start:prod`
- Swagger: http://localhost:8181/api (NODE_ENV != production 時)
