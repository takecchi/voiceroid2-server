import { ApiProperty } from '@nestjs/swagger';

export class VoiceDbList {
  @ApiProperty({
    type: [String],
    description:
      'インストールされているボイスライブラリ (Voice/ サブフォルダ) 名の一覧。' +
      ' Talk / Speech リクエストの voice_db に渡せる値。',
  })
  voice_dbs: string[];
}

export class SpeakerList {
  @ApiProperty({
    type: String,
    description: '対象のボイスライブラリ名 (voice_db)',
  })
  voice_db: string;

  @ApiProperty({
    type: [String],
    description: 'そのボイスライブラリに含まれる話者名 (voice_name)',
  })
  speakers: string[];
}
