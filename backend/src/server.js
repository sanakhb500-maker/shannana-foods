import { env } from './config/env.js'
import { prisma } from './config/prisma.js'
import { createApp } from './app.js'

const app = createApp()

// فحص الاتصال بقاعدة البيانات عند الإقلاع:
//   في التطوير: تحذير فقط (الخادم يعمل)،   في الإنتاج: إيقاف التطبيق.
try {
  await prisma.$queryRaw`SELECT 1`
  console.log('✅ PostgreSQL: متصل')
} catch (err) {
  console.error(`❌ PostgreSQL: تعذّر الاتصال — ${err.message.trim().split('\n').pop()}`)
  if (env.NODE_ENV === 'production') process.exit(1)
  console.error('   (يستمر الخادم في وضع التطوير؛ تحقق من DATABASE_URL وأن PostgreSQL يعمل)')
}

const server = app.listen(env.PORT, () => {
  console.log(`🚀 http://localhost:${env.PORT}  (${env.NODE_ENV})`)
})

server.on('error', (err) => {
  console.error(err.code === 'EADDRINUSE' ? `❌ المنفذ ${env.PORT} مستخدم، غيّر PORT في .env` : err)
  process.exit(1)
})

// إيقاف نظيف: أنهِ الطلبات الجارية ثم أغلق اتصال القاعدة
let shuttingDown = false
async function shutdown(signal) {
  if (shuttingDown) return
  shuttingDown = true
  console.log(`\n${signal}: إيقاف الخادم...`)
  server.close(async () => {
    await prisma.$disconnect()
    process.exit(0)
  })
  setTimeout(() => process.exit(1), 10_000).unref()
}
process.on('SIGINT', () => shutdown('SIGINT'))
process.on('SIGTERM', () => shutdown('SIGTERM'))

process.on('unhandledRejection', (reason) => console.error('unhandledRejection:', reason))
