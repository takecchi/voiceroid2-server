import {
  ExceptionFilter,
  Catch,
  ArgumentsHost,
  HttpException,
  Logger,
  InternalServerErrorException,
  HttpStatus,
} from '@nestjs/common';
import { Response } from 'express';

@Catch()
export class GlobalExceptionFilter implements ExceptionFilter {
  private readonly logger = new Logger(GlobalExceptionFilter.name);

  catch(exception: unknown, host: ArgumentsHost) {
    const ctx = host.switchToHttp();
    const response = ctx.getResponse<Response | undefined>();
    const request = ctx.getRequest<Request | undefined>();

    if (
      exception &&
      typeof exception === 'object' &&
      'message' in exception &&
      exception['message'] === 'request aborted'
    ) {
      this.logger.warn(`Request aborted: ${request?.url ?? 'N/A'}`);
      return;
    }

    if (exception && typeof exception === 'object' && 'type' in exception) {
      if (exception['type'] === 'entity.too.large') {
        this.logger.error(`Payload too large: ${request?.url ?? 'N/A'}`);
        if (response) {
          response.status(HttpStatus.PAYLOAD_TOO_LARGE).json({
            statusCode: HttpStatus.PAYLOAD_TOO_LARGE,
            message: 'Payload Too Large',
          });
        }
        return;
      }
    }

    if (
      exception instanceof HttpException &&
      !(exception instanceof InternalServerErrorException)
    ) {
      const status = exception.getStatus();
      if (response) {
        response.status(status).json(exception.getResponse());
      }
    } else {
      const status = 500;

      if (exception instanceof Error) {
        const text = `Status: ${status}\nMessage: ${exception.message}\nURL: ${request?.url ? request.url : 'N/A'}\nBody: ${request?.body ? JSON.stringify(request.body) : 'N/A'}`;
        this.logger.error(`${text}`, exception.stack);
      } else {
        const text = `Status: ${status}\nMessage: ${JSON.stringify(exception)}\nURL: ${request?.url ?? 'N/A'}`;
        this.logger.error(text);
      }

      if (response) {
        response.status(500).json({
          statusCode: 500,
          message: 'Internal Server Error',
        });
      }
    }
  }
}
