# Shannana Foods — Backend

Express 5 + Prisma 7 + PostgreSQL (ES Modules، بدون TypeScript في كودنا).

## المتطلبات
- Node.js 22.18 أو أحدث (عندك v24 ✔)
- PostgreSQL يعمل على المنفذ 5432

## التشغيل لأول مرة
```bash
# 1) أنشئ قاعدة البيانات (مرة واحدة)
psql -U postgres -c "CREATE DATABASE shannana_foods ENCODING 'UTF8' TEMPLATE template0;"

# 2) من داخل مجلد backend/
npm install

# 3) عدّل .env: ضع كلمة مرور PostgreSQL مكان YOUR_PASSWORD

# 4) اختبر الاتصال ثم شغّل الخادم
npm run db:check
npm run dev
```
ثم افتح: http://localhost:3000/api/health

## الأوامر
| الأمر | الوظيفة |
|---|---|
| `npm run dev` | تشغيل مع إعادة تشغيل تلقائية (يولّد Prisma Client أولًا) |
| `npm start` | تشغيل عادي (للإنتاج) |
| `npm run db:check` | اختبار اتصال Prisma بـ PostgreSQL |
| `npm run db:generate` | توليد Prisma Client يدويًا |
| `npm run db:validate` | التحقق من schema.prisma |
| `npm run db:migrate` | تطبيق migrations على قاعدة التطوير (`prisma migrate dev`) |
| `npm run db:migrate:deploy` | تطبيق migrations في الإنتاج (بدون توليد جديدة) |
| `npm run db:status` | حالة migrations (مطبَّقة / معلّقة) |
| `npm run db:verify` | فحص الجداول وenums وFK وCHECK وindexes مقابل المعتمد |
| `npm run db:drift` | يقارن القاعدة بـ schema.prisma (خروج 0 = مطابقة تامة) |

## البنية
```
backend/
├── prisma/schema.prisma      النموذج المعتمد (PHASE 2)
├── prisma.config.ts          إعدادات Prisma 7 + رابط القاعدة
├── scripts/check-db.js       اختبار الاتصال
└── src/
    ├── server.js             نقطة الإقلاع + الإيقاف النظيف
    ├── app.js                إعداد Express (Helmet, CORS, rate limit, static)
    ├── config/               env.js (تحقق Zod) + prisma.js
    ├── routes/ controllers/ services/ validators/
    ├── middleware/           notFound + errorHandler
    ├── utils/                AppError + apiResponse
    └── generated/            (يُولَّد تلقائيًا — لا يُرفع إلى Git)
```
الواجهة الأمامية تُخدَم من المجلد `../frontend` (بجانب backend/) إن وُجد.
