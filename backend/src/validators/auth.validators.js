import { z } from 'zod'

// POST /api/auth/login
export const loginBody = z.object({
  email: z
    .string({ error: 'البريد الإلكتروني مطلوب' })
    .trim()
    .toLowerCase()
    .pipe(z.email('صيغة البريد الإلكتروني غير صحيحة').max(254, 'البريد الإلكتروني طويل جدًا')),
  // لا نقيّد الحد الأدنى هنا: التحقق الفعلي يتم بمقارنة الـ hash. الحد الأعلى يمنع إرهاق Argon2.
  password: z
    .string({ error: 'كلمة المرور مطلوبة' })
    .min(1, 'كلمة المرور مطلوبة')
    .max(128, 'كلمة المرور طويلة جدًا'),
})
