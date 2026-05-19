import { Module } from '@nestjs/common';
import { Voiceroid2Controller } from './voiceroid2.controller';
import { Voiceroid2Service } from './voiceroid2.service';
import { Voiceroid2Cli } from './voiceroid2-cli';

@Module({
  controllers: [Voiceroid2Controller],
  providers: [Voiceroid2Service, Voiceroid2Cli],
})
export class Voiceroid2Module {}
