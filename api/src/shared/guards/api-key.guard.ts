import {
  CanActivate,
  ExecutionContext,
  Injectable,
  UnauthorizedException,
} from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import type { Request } from 'express';

export const API_KEY_HEADER = 'x-api-key';
export const API_KEY_ENV = 'API_KEY';
export const API_KEY_SECURITY_NAME = 'api-key';

/**
 * API キー認証ガード。@UseGuards(ApiKeyGuard) で保護したいエンドポイントに付ける。
 *
 * - API_KEY 環境変数が未設定 → 認証無効として素通り
 * - API_KEY 環境変数が設定 → X-API-Key ヘッダーと完全一致しなければ 401
 */
@Injectable()
export class ApiKeyGuard implements CanActivate {
  constructor(private readonly config: ConfigService) {}

  canActivate(context: ExecutionContext): boolean {
    const expected = this.config.get<string>(API_KEY_ENV);
    if (!expected) {
      return true;
    }

    const request = context.switchToHttp().getRequest<Request>();
    const provided = request.header(API_KEY_HEADER);
    if (provided !== expected) {
      throw new UnauthorizedException('Invalid API key');
    }
    return true;
  }
}
