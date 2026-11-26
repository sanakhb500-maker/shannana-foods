// Prisma 7 configuration — lives next to package.json (backend/prisma.config.ts).
// Prisma 7 no longer reads .env by itself, so dotenv is loaded explicitly.
// The connection string comes only from the DATABASE_URL environment variable.
import 'dotenv/config';
import { defineConfig, env } from 'prisma/config';

export default defineConfig({
  schema: 'prisma/schema.prisma',
  migrations: {
    path: 'prisma/migrations',
  },
  datasource: {
    url: env('DATABASE_URL'),
  },
});
