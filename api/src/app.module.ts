import { Module } from '@nestjs/common';
import { SharedModule } from '@/shared/shared.module';
import { Voiceroid2Module } from '@/voiceroid2/voiceroid2.module';

@Module({
  imports: [SharedModule, Voiceroid2Module],
})
export class AppModule {}
