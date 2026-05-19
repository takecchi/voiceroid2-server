import { Global, Module } from '@nestjs/common';
import { LoggingService } from '@/shared/logging.service';
import { ConfigModule } from '@nestjs/config';

@Global()
@Module({
  imports: [
    ConfigModule.forRoot({
      isGlobal: true,
      envFilePath: [
        '.env.production',
        '.env.develop',
        '.env.local',
        '.env',
        '.env.default',
      ],
    }),
  ],
  providers: [LoggingService],
  exports: [LoggingService],
})
export class SharedModule {}
