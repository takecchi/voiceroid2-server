import { ApiProperty } from '@nestjs/swagger';

export class SpeakerList {
  @ApiProperty({ type: [String], description: '利用可能な話者名の一覧' })
  speakers: string[];
}
