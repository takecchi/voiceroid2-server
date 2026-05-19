import { ValidationPipe } from '@nestjs/common';

export function createValidationPipe() {
  return new ValidationPipe({
    // eslint-disable-next-line
    transformerPackage: require('@takecchi/class-transformer'),
    transform: true,
    transformOptions: { enableImplicitConversion: true },
    whitelist: true,
    forbidNonWhitelisted: true,
  });
}
