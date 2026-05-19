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
    expect(result.voice_db).toBeUndefined();
    expect(result.voice_name).toBeUndefined();
  });

  it('should pass with text + voice_db + voice_name', async () => {
    const result = await transform({
      text: 'こんにちは',
      voice_db: 'yukari_44',
      voice_name: '結月ゆかり',
    });
    expect(result.text).toBe('こんにちは');
    expect(result.voice_db).toBe('yukari_44');
    expect(result.voice_name).toBe('結月ゆかり');
  });

  it('should reject when text is missing', async () => {
    await expect(transform({})).rejects.toThrow(BadRequestException);
  });

  it('should reject when text is empty', async () => {
    await expect(transform({ text: '' })).rejects.toThrow(BadRequestException);
  });

  it('should convert null optional voice_db / voice_name to undefined', async () => {
    const result = await transform({
      text: 'テスト',
      voice_db: null,
      voice_name: null,
    });
    expect(result.voice_db).toBeUndefined();
    expect(result.voice_name).toBeUndefined();
  });

  it('should reject unknown properties', async () => {
    await expect(
      transform({ text: 'テスト', unknown_field: 'value' }),
    ).rejects.toThrow(BadRequestException);
  });

  describe('tuning params', () => {
    it('should pass with all tuning params', async () => {
      const result = await transform({
        text: 'てすと',
        volume: 1.5,
        speed: 0.8,
        pitch: 1.2,
        intonation: 0.7,
      });
      expect(result.volume).toBe(1.5);
      expect(result.speed).toBe(0.8);
      expect(result.pitch).toBe(1.2);
      expect(result.intonation).toBe(0.7);
    });

    it('should leave undefined when tuning params are not specified', async () => {
      const result = await transform({ text: 'てすと' });
      expect(result.volume).toBeUndefined();
      expect(result.speed).toBeUndefined();
      expect(result.pitch).toBeUndefined();
      expect(result.intonation).toBeUndefined();
    });

    it('should convert null tuning params to undefined', async () => {
      const result = await transform({
        text: 'てすと',
        volume: null,
        speed: null,
        pitch: null,
        intonation: null,
      });
      expect(result.volume).toBeUndefined();
      expect(result.speed).toBeUndefined();
      expect(result.pitch).toBeUndefined();
      expect(result.intonation).toBeUndefined();
    });

    it('should accept per-param maximum values', async () => {
      const result = await transform({
        text: 'てすと',
        volume: 5.0,
        speed: 4.0,
        pitch: 2.0,
        intonation: 2.0,
      });
      expect(result.volume).toBe(5.0);
      expect(result.speed).toBe(4.0);
      expect(result.pitch).toBe(2.0);
      expect(result.intonation).toBe(2.0);
    });

    it('should accept per-param minimum values', async () => {
      const result = await transform({
        text: 'てすと',
        volume: 0,
        speed: 0.5,
        pitch: 0.5,
        intonation: 0,
      });
      expect(result.volume).toBe(0);
      expect(result.speed).toBe(0.5);
      expect(result.pitch).toBe(0.5);
      expect(result.intonation).toBe(0);
    });

    it.each([
      ['volume', -0.1],
      ['volume', 5.1],
      ['speed', 0.4],
      ['speed', 4.1],
      ['pitch', 0.4],
      ['pitch', 2.1],
      ['intonation', -0.1],
      ['intonation', 2.1],
    ])('should reject %s = %s (out of range)', async (field, value) => {
      await expect(
        transform({ text: 'てすと', [field]: value }),
      ).rejects.toThrow(BadRequestException);
    });

    it('should reject non-numeric tuning param', async () => {
      await expect(
        transform({ text: 'てすと', pitch: 'fast' }),
      ).rejects.toThrow(BadRequestException);
    });
  });
});
