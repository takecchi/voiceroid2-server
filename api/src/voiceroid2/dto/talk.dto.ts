import { ApiProperty } from '@nestjs/swagger';
import { IsNotEmpty, IsOptional, IsString } from 'class-validator';
import { NullToUndefined } from '@/shared/decorators/NullToUndefined';
import { VoiceTuningParams } from './voice-tuning.dto';

export class TalkRequest extends VoiceTuningParams {
  @ApiProperty({
    type: String,
    description: '読み上げるテキスト',
  })
  @IsString()
  @IsNotEmpty()
  text: string;

  @ApiProperty({
    type: String,
    required: false,
    description:
      'ボイスライブラリ名 (Voice/ フォルダ名、例: kiritan_44)。' +
      ' 省略時は環境変数 VOICEROID2_DEFAULT_VOICE_DB が使われる。',
  })
  @NullToUndefined()
  @IsOptional()
  @IsString()
  voice_db?: string;

  @ApiProperty({
    type: String,
    required: false,
    description:
      '話者名 (例: 結月ゆかり)。voice_db 内に含まれる話者を指定する。' +
      ' 省略時は voice_db の先頭話者が使われる。',
  })
  @NullToUndefined()
  @IsOptional()
  @IsString()
  voice_name?: string;
}

export class TalkResult {
  @ApiProperty({ type: Boolean, description: '発話を受け付けたか' })
  success: boolean;

  @ApiProperty({ type: String, description: '結果メッセージ' })
  message: string;
}
