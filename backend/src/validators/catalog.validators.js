import { z } from 'zod'

// نفس صيغة قيد CHECK في قاعدة البيانات: ^[a-z0-9]+(-[a-z0-9]+)*$
const slug = z
  .string({ error: 'قيمة التصنيف غير صحيحة' })
  .trim()
  .toLowerCase()
  .min(1, 'قيمة التصنيف غير صحيحة')
  .max(100, 'قيمة التصنيف طويلة جدًا')
  .regex(/^[a-z0-9]+(-[a-z0-9]+)*$/, 'صيغة التصنيف غير صحيحة')

// lang اختياري (ar افتراضيًا): تُعرض الأسماء والأوصاف بالإنجليزية عند en مع رجوع للعربية إن غابت الترجمة
const lang = z.enum(['ar', 'en'], { error: 'اللغة غير مدعومة' }).optional()

// GET /api/products?category=<slug>&lang=en
export const productsQuery = z.object({ category: slug.optional(), lang })

// GET /api/categories?lang=en
export const categoriesQuery = z.object({ lang })
