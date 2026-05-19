import { ApiProperty } from '@nestjs/swagger';
import { IsNotEmpty, IsOptional, IsString } from 'class-validator';
import { NullToUndefined } from '@/shared/decorators/NullToUndefined';

export class SynthesizeSpeechRequest {
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
      '話者名 (例: 結月ゆかり)。指定するとVOICEROID2の話者切替記号 "<speaker>＞" を先頭に挿入します。',
  })
  @NullToUndefined()
  @IsOptional()
  @IsString()
  speaker?: string;
}
