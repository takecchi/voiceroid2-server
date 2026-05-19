import { dump } from 'js-yaml';
import fs from 'fs';
import path from 'path';
import { SwaggerModule } from '@nestjs/swagger';
import { createApp, createConfig } from '@/app';

const exportOpenAPI = async () => {
  const app = await createApp();
  const config = createConfig();
  const document = SwaggerModule.createDocument(app, config);
  const yamlDocument = dump(document, {
    skipInvalid: true,
    noRefs: true,
  });

  let currentDir = __dirname;
  if (__dirname.endsWith('dist')) {
    currentDir = path.resolve(__dirname, '..');
  }

  const yamlPath = path.join(currentDir, 'openapi.yaml');
  fs.writeFileSync(yamlPath, yamlDocument);
  process.exit(0);
};

void (async () => await exportOpenAPI())();
