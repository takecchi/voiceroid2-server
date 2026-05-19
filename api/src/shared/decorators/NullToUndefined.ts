import { Transform } from '@takecchi/class-transformer';

/**
 * `null` の場合は `undefined` に変換するデコレーター
 * @IsOptional と組み合わせて使用できます。
 * DTOのoptionalプロパティで、undefinedは許容するがnullは許容しない場合（`hoge?: string`）は@NullToUndefined()デコレータを付けてください
 */
export function NullToUndefined() {
  return Transform(({ value }: { value: unknown }) =>
    value === null ? undefined : value,
  );
}
