import { BadRequestException } from '@nestjs/common';
import { createValidationPipe } from '@/shared/utils/app.utils';
import { TalkRequest } from './talk.dto';

describe('TalkRequest', () => {
  const pipe = createValidationPipe();
  const meta = { type: 'body' as const, metatype: TalkRequest };

  // pipe.transform は any を返すが、metatype を渡しているので
  // 実 runtime では class-transformer 経由で TalkRequest のインスタンスが返る。
  // 型システムが推論できない事実をここで宣言する。
  const transform = (input: Record<string, unknown>): Promise<TalkRequest> =>
    pipe.transform(input, meta) as Promise<TalkRequest>;

  it('should pass with valid text only', async () => {
    const result = await transform({ text: 'てすと' });
    expect(result.text).toBe('てすと');
    expect(result.speaker).toBeUndefined();
  });

  it('should pass with text + speaker', async () => {
    const result = await transform({
      text: 'こんにちは',
      speaker: '結月ゆかり',
    });
    expect(result.text).toBe('こんにちは');
    expect(result.speaker).toBe('結月ゆかり');
  });

  it('should reject when text is missing', async () => {
    await expect(transform({})).rejects.toThrow(BadRequestException);
  });

  it('should reject when text is empty', async () => {
    await expect(transform({ text: '' })).rejects.toThrow(BadRequestException);
  });

  it('should convert null optional speaker to undefined', async () => {
    const result = await transform({ text: 'テスト', speaker: null });
    expect(result.speaker).toBeUndefined();
  });

  it('should reject unknown properties', async () => {
    await expect(
      transform({ text: 'テスト', unknown_field: 'value' }),
    ).rejects.toThrow(BadRequestException);
  });
});
