import { Global, Module } from '@nestjs/common';
import { LoggingService } from '@/shared/logging.service';
import { ConfigModule } from '@nestjs/config';
import { ApiKeyGuard } from '@/shared/guards/api-key.guard';

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
  providers: [LoggingService, ApiKeyGuard],
  exports: [LoggingService, ApiKeyGuard],
})
export class SharedModule {}
