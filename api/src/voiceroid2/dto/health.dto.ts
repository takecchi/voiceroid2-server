import { ApiProperty } from '@nestjs/swagger';

export class Health {
  @ApiProperty({ type: String, description: 'サーバーの状態' })
  status: string;
}
