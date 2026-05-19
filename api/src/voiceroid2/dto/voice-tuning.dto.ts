import { ApiProperty } from '@nestjs/swagger';
import { IsNumber, IsOptional, Max, Min } from 'class-validator';
import { NullToUndefined } from '@/shared/decorators/NullToUndefined';

/**
 * VOICEROID2 のマスター音声効果パラメータ (音量・話速・高さ・抑揚)。
 * Talk / Synthesize リクエスト両方の継承元として使う。
 *
 * 範囲は VOICEROID2 GUI のスライダー上限/下限に合わせている。
 * すべて任意。未指定時は VOICEROID2 既定の 1.0 倍として helper 側で送信する
 * (前回のリクエストの値が VOICEROID2 に残るのを避ける目的)。
 */
export class VoiceTuningParams {
  @ApiProperty({
    type: Number,
    required: false,
    default: 1.0,
    minimum: 0,
    maximum: 5.0,
    description: '音量倍率 (0 〜 5.0)。未指定時 1.0。',
  })
  @NullToUndefined()
  @IsOptional()
  @IsNumber()
  @Min(0)
  @Max(5.0)
  volume?: number;

  @ApiProperty({
    type: Number,
    required: false,
    default: 1.0,
    minimum: 0.5,
    maximum: 4.0,
    description: '話速倍率 (0.5 〜 4.0)。未指定時 1.0。',
  })
  @NullToUndefined()
  @IsOptional()
  @IsNumber()
  @Min(0.5)
  @Max(4.0)
  speed?: number;

  @ApiProperty({
    type: Number,
    required: false,
    default: 1.0,
    minimum: 0.5,
    maximum: 2.0,
    description: '高さ (ピッチ) 倍率 (0.5 〜 2.0)。未指定時 1.0。',
  })
  @NullToUndefined()
  @IsOptional()
  @IsNumber()
  @Min(0.5)
  @Max(2.0)
  pitch?: number;

  @ApiProperty({
    type: Number,
    required: false,
    default: 1.0,
    minimum: 0,
    maximum: 2.0,
    description: '抑揚 (ピッチレンジ) 倍率 (0 〜 2.0)。未指定時 1.0。',
  })
  @NullToUndefined()
  @IsOptional()
  @IsNumber()
  @Min(0)
  @Max(2.0)
  intonation?: number;
}
