import { Body, Controller, Get, Post, Res } from '@nestjs/common';
import {
  ApiBadRequestResponse,
  ApiCreatedResponse,
  ApiOkResponse,
  ApiOperation,
  ApiProduces,
  ApiTags,
} from '@nestjs/swagger';
import type { Response } from 'express';
import { Voiceroid2Service } from './voiceroid2.service';
import { Health } from './dto/health.dto';
import { WorkerStatus } from './dto/status.dto';
import { SpeakerList } from './dto/speaker-list.dto';
import { SynthesizeSpeechRequest } from './dto/synthesize-speech.dto';
import { TalkRequest, TalkResponse } from './dto/talk.dto';

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
  @ApiOperation({
    operationId: 'getStatus',
    summary: 'ワーカーのステータスを取得',
  })
  @ApiOkResponse({ type: WorkerStatus })
  status(): WorkerStatus {
    return this.service.getStatus();
  }

  @Get('speakers')
  @ApiOperation({
    operationId: 'getSpeakers',
    summary: '利用可能な話者一覧を取得',
  })
  @ApiOkResponse({ type: SpeakerList })
  async listSpeakers(): Promise<SpeakerList> {
    return { speakers: await this.service.listSpeakers() };
  }

  @Post('talk')
  @ApiOperation({
    operationId: 'talk',
    summary: 'テキストをVOICEROID2に発話させる (スピーカー出力のみ)',
  })
  @ApiOkResponse({ type: TalkResponse })
  @ApiBadRequestResponse({ description: '不正なリクエスト' })
  async talk(@Body() request: TalkRequest): Promise<TalkResponse> {
    await this.service.talk({
      text: request.text,
      speaker: request.speaker,
    });
    return { success: true, message: `Talked: ${request.text}` };
  }

  @Post('speech')
  @ApiOperation({
    operationId: 'synthesizeSpeech',
    summary: 'テキストから音声を合成してWAVを返す',
  })
  @ApiCreatedResponse({ description: '音声合成されたWAVファイル' })
  @ApiBadRequestResponse({ description: '不正なリクエスト' })
  @ApiProduces('audio/wav')
  async synthesize(
    @Body() request: SynthesizeSpeechRequest,
    @Res() res: Response,
  ) {
    const wav = await this.service.synthesize({
      text: request.text,
      speaker: request.speaker,
    });

    res.set({
      'Content-Type': 'audio/wav',
      'Content-Length': wav.length,
    });
    res.send(wav);
  }
}
