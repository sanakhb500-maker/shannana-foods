// يختبر اتصال Prisma بـ PostgreSQL:  npm run db:check
import { prisma } from '../src/config/prisma.js'

try {
  const [info] = await prisma.$queryRaw`
    SELECT current_database() AS db, current_user AS "user", split_part(version(), ' ', 2) AS pg_version`
  const [{ n }] = await prisma.$queryRaw`
    SELECT count(*)::int AS n FROM information_schema.tables
    WHERE table_schema = 'public' AND table_name <> '_prisma_migrations'`

  console.log('✅ الاتصال ناجح')
  console.log(`   قاعدة البيانات : ${info.db}`)
  console.log(`   المستخدم       : ${info.user}`)
  console.log(`   إصدار PostgreSQL: ${info.pg_version}`)
  console.log(`   عدد الجداول    : ${n}  ${n === 0 ? '(طبيعي قبل PHASE 4 — لا migration بعد)' : ''}`)
} catch (err) {
  console.error('❌ فشل الاتصال:', err.message)
  process.exitCode = 1
} finally {
  await prisma.$disconnect()
}
