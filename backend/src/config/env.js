// قراءة متغيرات البيئة والتحقق منها مرة واحدة عند الإقلاع.
// إن كان أي متغير ناقصًا أو خاطئًا يتوقف التطبيق فورًا برسالة واضحة (fail fast).
import 'dotenv/config'
import { z } from 'zod'

const schema = z.object({
  NODE_ENV: z.enum(['development', 'test', 'production']).default('development'),
  PORT: z.coerce.number().int().min(1).max(65535).default(3000),
  DATABASE_URL: z
    .string()
    .min(1, 'مطلوب')
    .refine((v) => /^postgres(ql)?:\/\//.test(v), 'يجب أن يبدأ بـ postgresql://')
    .refine((v) => !v.includes('YOUR_PASSWORD'), 'ما زالت فيه كلمة المرور الوهمية YOUR_PASSWORD — ضع كلمة مرور PostgreSQL الحقيقية'),
  JWT_SECRET: z.string().min(32, 'يجب أن يكون 32 حرفًا على الأقل'),
  WHATSAPP_NUMBER: z.string().regex(/^\d{8,15}$/, 'أرقام فقط (8–15 رقمًا) بدون + أو مسافات'),
  ORDER_WHATSAPP_NUMBER: z.string().regex(/^\d{8,15}$/, 'أرقام فقط (8–15 رقمًا) بدون + أو مسافات'),
  CORS_ORIGINS: z
    .string()
    .default('')
    .transform((v) => v.split(',').map((s) => s.trim()).filter(Boolean)),
  TRUST_PROXY: z.coerce.number().int().min(0).default(0),
})

const parsed = schema.safeParse(process.env)

if (!parsed.success) {
  console.error('\n❌ إعدادات البيئة (.env) غير صحيحة:\n')
  for (const issue of parsed.error.issues) {
    console.error(`  • ${issue.path.join('.')}: ${issue.message}`)
  }
  console.error('\nراجع الملف backend/.env (القالب: backend/.env.example)\n')
  process.exit(1)
}

export const env = Object.freeze(parsed.data)
