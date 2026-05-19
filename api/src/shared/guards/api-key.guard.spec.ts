import { UnauthorizedException, ExecutionContext } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { ApiKeyGuard, API_KEY_HEADER, API_KEY_ENV } from './api-key.guard';

function createContext(headerValue?: string): ExecutionContext {
  const request = {
    header: jest.fn((name: string) =>
      name.toLowerCase() === API_KEY_HEADER ? headerValue : undefined,
    ),
  };
  return {
    switchToHttp: () => ({ getRequest: () => request }),
  } as unknown as ExecutionContext;
}

function createGuard(envValue: string | undefined): ApiKeyGuard {
  const config = {
    get: jest.fn((key: string) => (key === API_KEY_ENV ? envValue : undefined)),
  } as unknown as ConfigService;
  return new ApiKeyGuard(config);
}

describe('ApiKeyGuard', () => {
  it('API_KEY 未設定なら認証スキップして true', () => {
    const guard = createGuard(undefined);
    expect(guard.canActivate(createContext())).toBe(true);
  });

  it('API_KEY 設定 + ヘッダー一致なら true', () => {
    const guard = createGuard('secret');
    expect(guard.canActivate(createContext('secret'))).toBe(true);
  });

  it('API_KEY 設定 + ヘッダー不一致なら UnauthorizedException', () => {
    const guard = createGuard('secret');
    expect(() => guard.canActivate(createContext('wrong'))).toThrow(
      UnauthorizedException,
    );
  });

  it('API_KEY 設定 + ヘッダー無しなら UnauthorizedException', () => {
    const guard = createGuard('secret');
    expect(() => guard.canActivate(createContext(undefined))).toThrow(
      UnauthorizedException,
    );
  });

  it('空文字の API_KEY は未設定扱い', () => {
    const guard = createGuard('');
    expect(guard.canActivate(createContext(undefined))).toBe(true);
  });
});
