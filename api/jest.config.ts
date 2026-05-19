import type { Config } from '@jest/types';
import dotenv from 'dotenv';

dotenv.config({ path: '.env.test' });

const config: Config.InitialOptions = {
  maxWorkers: '3',
  moduleFileExtensions: ['js', 'json', 'ts'],
  rootDir: 'src',
  testRegex: '.*\\.spec\\.ts$',
  transform: {
    '^.+\\.(t|j)s$': 'ts-jest',
  },
  transformIgnorePatterns: ['node_modules/(?!(p-map|@mastra/core)/)'],
  collectCoverageFrom: ['**/*.(t|j)s'],
  coverageDirectory: '../coverage',
  testEnvironment: 'node',
  moduleNameMapper: {
    '^@/(.*)$': '<rootDir>/$1',
  },
  testTimeout: 30_000,
  setupFilesAfterEnv: [],
};

export default config;
