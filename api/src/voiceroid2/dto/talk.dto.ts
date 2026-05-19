import { ApiProperty } from '@nestjs/swagger';
import { IsNotEmpty, IsOptional, IsString } from 'class-validator';
import { NullToUndefined } from '@/shared/decorators/NullToUndefined';
import { VoiceTuningParams } from './voice-tuning.dto';

export class TalkRequest extends VoiceTuningParams {
  @ApiProperty({
    type: String,
    description:
      '読み上げるテキスト。SimpleVoiceroid2Proxy互換のコマンド (<clear>, <pause>, <resume>, <interrupt_enable>, <interrupt_disable>) も使えます。',
  })
  @IsString()
  @IsNotEmpty()
  text: string;

  @ApiProperty({
    type: String,
    required: false,
    description: '話者名 (例: 結月ゆかり)',
  })
  @NullToUndefined()
  @IsOptional()
  @IsString()
  speaker?: string;
}

export class TalkResult {
  @ApiProperty({ type: Boolean, description: '発話を受け付けたか' })
  success: boolean;

  @ApiProperty({ type: String, description: '結果メッセージ' })
  message: string;
}
