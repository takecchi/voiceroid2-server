import { ApiProperty } from '@nestjs/swagger';

export class WorkerStatus {
  @ApiProperty({
    type: String,
    description: 'ワーカーの状態',
    enum: ['idle', 'busy'],
  })
  status: 'idle' | 'busy';

  @ApiProperty({
    type: Number,
    description: '処理中 + キュー待ちのリクエスト数',
  })
  queue_length: number;
}
