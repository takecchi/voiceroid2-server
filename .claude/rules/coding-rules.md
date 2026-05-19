---
trigger: always_on
description:
globs:
---

# バックエンド コーディングルール

## ディレクトリ構成

```
api/src/
├── {feature}/        # 機能ごとのディレクトリ（Module, Service, Controller, DTO等）
│   ├── {feature}.module.ts
│   ├── {feature}.service.ts
│   ├── {feature}.controller.ts
│   └── dto/
└── shared/           # グローバル共有（デコレータ, ユーティリティ）
```

## 命名規則

### ファイル名

- ケバブケース: `{name}.{type}.ts`
- 例: `voiceroid2.service.ts`, `voiceroid2.controller.ts`, `synthesize-speech.dto.ts`

### クラス名

- パスカルケース: `{Name}{Type}`
- 例: `Voiceroid2Service`, `Voiceroid2Controller`, `SynthesizeSpeechRequest`

### DTO 命名規則

- **クラス名に `Dto` サフィックスは付けない。** リソースそのものの名前を使う
- **ファイル名には `.dto.ts` サフィックスを付ける**
- **DTO は `{feature}/dto/` に置く**
- レスポンス DTO — クラス名: `Speaker`, `SpeechResult`。ファイル名: `speaker.dto.ts`, `speech-result.dto.ts`
- リクエスト DTO — クラス名: `SynthesizeSpeechRequest`。ファイル名: `synthesize-speech.dto.ts`
- **プロパティはスネークケース**（例: `speaker_name`, `display_name`）
- クラス名はパスカルケース

### DTO デコレータルール

**重要: DTOはクライアントとバックエンドでやり取りするためのインターフェース定義。**

- `@ApiProperty()` で REST API 仕様を記述する
  - **必ず明示的に `type` を指定する**（例: `@ApiProperty({ type: String })`）
  - **Date型プロパティの type は Date を指定**（例: `@ApiProperty({ type: Date }`）
  - **description を必ず記載**（例: `@ApiProperty({ type: String, description: 'ナレーター名' }`）
- TypeScript の型定義と `@ApiProperty` の対応：
  - `field?: string` → `@ApiProperty({ type: String, required: false })`
  - `field: string` → `@ApiProperty({ type: String })`（required は省略可）
  - `field: string | null` → `@ApiProperty({ type: String, nullable: true })`
  - `field?: string | null` → `@ApiProperty({ type: String, required: false, nullable: true })`
- `@NullToUndefined()` デコレータの使用ルール：
  - **`field?: string` のように undefined は許容するが null は許容しない optional フィールドに使用**
  - クライアントから送られてくる null を undefined に変換する
  - 使用する場合、TypeScript の型は `field?: string` とする（null を含めない）
  - `@ApiProperty` では `required: false` のみ指定（nullable は付けない）
- ネストした DTO 型のプロパティには必ず `@Type(() => Xxx)` デコレータを付ける

## RESTful API 設計

### エンドポイント規約

- リソース名は複数形
- パスパラメータはスネークケース

### HTTP メソッドと操作の対応

| 操作     | メソッド | パス                      | ステータス |
| -------- | -------- | ------------------------- | ---------- |
| 一覧取得 | GET      | `/resources`              | 200        |
| 単体取得 | GET      | `/resources/:id`          | 200        |
| 作成     | POST     | `/resources`              | 201        |
| 更新     | PATCH    | `/resources/:id`          | 200        |
| 削除     | DELETE   | `/resources/:id`          | 204        |
| 特殊操作 | POST     | `/resources/:id/{action}` | 200        |

### Swagger

- 全エンドポイントに `@ApiOperation({ operationId, summary })` を付ける
- `operationId` は `{method}{Resource}` 形式（例: `getSpeakers`, `synthesizeSpeech`）
- `@ApiTags('{resource}')` でグループ化

## テスト

- ロジックを含むコードには必ずテストを書く
- **リクエスト DTO には必ずバリデーションテストを書く**
  - `createValidationPipe()` で pipe を取得し、`pipe.transform(input, { type: 'body', metatype: XxxRequest })` で検証する
  - 不正入力は `BadRequestException` がスローされることを確認する
- テストファイルは同ディレクトリに `*.spec.ts`
- ピュアロジックは `shared/utils/` に分離してユニットテスト

## その他

- 型・関数・クラスのエクスポートは定義に直接 `export` を付ける。末尾にまとめて `export { ... }` と書かない
- `any` は禁止
- 1ファイル500行超は分割を検討
