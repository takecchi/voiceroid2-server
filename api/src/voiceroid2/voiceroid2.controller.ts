import {
  Body,
  Controller,
  Get,
  Param,
  Post,
  Res,
  UseGuards,
} from '@nestjs/common';
import {
  ApiBadRequestResponse,
  ApiCreatedResponse,
  ApiOkResponse,
  ApiOperation,
  ApiParam,
  ApiProduces,
  ApiSecurity,
  ApiTags,
  ApiUnauthorizedResponse,
} from '@nestjs/swagger';
import type { Response } from 'express';
import { Voiceroid2Service } from './voiceroid2.service';
import { Health } from './dto/health.dto';
import { WorkerStatus } from './dto/status.dto';
import { SpeakerList, VoiceDbList } from './dto/speaker-list.dto';
import { SynthesizeSpeechRequest } from './dto/synthesize-speech.dto';
import { TalkRequest, TalkResult } from './dto/talk.dto';
import {
  ApiKeyGuard,
  API_KEY_SECURITY_NAME,
} from '@/shared/guards/api-key.guard';

@ApiTags('voiceroid2')
@Controller('voiceroid2')
export class Voiceroid2Controller {
  constructor(private readonly service: Voiceroid2Service) {}

  @Get('health')
  @ApiOperation({ operationId: 'getHealth', summary: 'ヘルスチェック' })
  @ApiOkResponse({ type: Health })
  health(): Health {
    return { status: 'ok' };
  }

  @Get('status')
  @UseGuards(ApiKeyGuard)
  @ApiSecurity(API_KEY_SECURITY_NAME)
  @ApiOperation({
    operationId: 'getStatus',
    summary: 'ワーカーのステータスを取得',
  })
  @ApiOkResponse({ type: WorkerStatus })
  @ApiUnauthorizedResponse({ description: 'API キーが不正' })
  status(): WorkerStatus {
    return this.service.getStatus();
  }

  @Get('voice-dbs')
  @UseGuards(ApiKeyGuard)
  @ApiSecurity(API_KEY_SECURITY_NAME)
  @ApiOperation({
    operationId: 'getVoiceDbs',
    summary: 'インストール済みのボイスライブラリ一覧を取得',
  })
  @ApiOkResponse({ type: VoiceDbList })
  @ApiUnauthorizedResponse({ description: 'API キーが不正' })
  async listVoiceDbs(): Promise<VoiceDbList> {
    return { voice_dbs: await this.service.listVoiceDbs() };
  }

  @Get('voice-dbs/:voice_db/speakers')
  @UseGuards(ApiKeyGuard)
  @ApiSecurity(API_KEY_SECURITY_NAME)
  @ApiOperation({
    operationId: 'getSpeakers',
    summary: '指定ボイスライブラリ内の話者名一覧を取得',
  })
  @ApiParam({
    name: 'voice_db',
    type: String,
    description: 'ボイスライブラリ名 (例: kiritan_44)',
  })
  @ApiOkResponse({ type: SpeakerList })
  @ApiUnauthorizedResponse({ description: 'API キーが不正' })
  async listSpeakers(
    @Param('voice_db') voice_db: string,
  ): Promise<SpeakerList> {
    return {
      voice_db,
      speakers: await this.service.listSpeakers(voice_db),
    };
  }

  @Post('talk')
  @UseGuards(ApiKeyGuard)
  @ApiSecurity(API_KEY_SECURITY_NAME)
  @ApiOperation({
    operationId: 'talk',
    summary: 'テキストをVOICEROID2に発話させる (スピーカー出力のみ)',
  })
  @ApiOkResponse({ type: TalkResult })
  @ApiBadRequestResponse({ description: '不正なリクエスト' })
  @ApiUnauthorizedResponse({ description: 'API キーが不正' })
  async talk(@Body() request: TalkRequest): Promise<TalkResult> {
    await this.service.talk({
      text: request.text,
      voice_db: request.voice_db,
      voice_name: request.voice_name,
      volume: request.volume,
      speed: request.speed,
      pitch: request.pitch,
      intonation: request.intonation,
    });
    return { success: true, message: `Talked: ${request.text}` };
  }

  @Post('speech')
  @UseGuards(ApiKeyGuard)
  @ApiSecurity(API_KEY_SECURITY_NAME)
  @ApiOperation({
    operationId: 'synthesizeSpeech',
    summary: 'テキストから音声を合成してWAVを返す',
  })
  @ApiCreatedResponse({ description: '音声合成されたWAVファイル' })
  @ApiBadRequestResponse({ description: '不正なリクエスト' })
  @ApiUnauthorizedResponse({ description: 'API キーが不正' })
  @ApiProduces('audio/wav')
  async synthesize(
    @Body() request: SynthesizeSpeechRequest,
    @Res() res: Response,
  ) {
    const wav = await this.service.synthesize({
      text: request.text,
      voice_db: request.voice_db,
      voice_name: request.voice_name,
      volume: request.volume,
      speed: request.speed,
      pitch: request.pitch,
      intonation: request.intonation,
    });

    res.set({
      'Content-Type': 'audio/wav',
      'Content-Length': wav.length,
    });
    res.send(wav);
  }
}
