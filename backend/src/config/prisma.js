// عميل Prisma الوحيد في التطبيق. Prisma 7 يحتاج adapter للاتصال بـ PostgreSQL.
// ملاحظة: الملف المولَّد هو TypeScript؛ Node (22.18+) يشغّله مباشرة بدون خطوة build.
import { PrismaPg } from '@prisma/adapter-pg'
import { PrismaClient } from '../generated/prisma/client.ts'
import { env } from './env.js'

const adapter = new PrismaPg({ connectionString: env.DATABASE_URL })

export const prisma = new PrismaClient({
  adapter,
  log: env.NODE_ENV === 'development' ? ['warn', 'error'] : ['error'],
})
