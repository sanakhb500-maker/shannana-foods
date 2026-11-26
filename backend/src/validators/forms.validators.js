import { z } from 'zod'
import { cleanText, normalizePhone } from '../utils/formGuards.js'
import { apiT } from '../i18n/index.js'

const blank = (v) => (typeof v === 'string' && v.trim() === '' ? undefined : v)

export const BUSINESS_TYPES = ['shop', 'distributor', 'wholesaler', 'restaurant', 'supermarket', 'other']

// مخططات نماذج الزوار حسب اللغة (رسائل الأخطاء بلغة الصفحة). تُبنى مرة لكل لغة وتُخزَّن.
function buildFormSchemas(lang) {
  const t = apiT(lang)
  const L = (k) => t(`label.${k}`)
  const text = (label, min, max) =>
    z.preprocess(
      (v) => (typeof v === 'string' ? cleanText(v) : v),
      z.string({ error: t('v.invalid', { label }) }).min(min, min <= 1 ? t('v.required', { label }) : t('v.tooShort', { label, min })).max(max, t('v.tooLong', { label, max })),
    )
  const optText = (label, max) => z.preprocess((v) => blank(typeof v === 'string' ? cleanText(v) : v), z.string({ error: t('v.invalid', { label }) }).max(max, t('v.tooLongPlain', { label })).optional())

  const PHONE_MSG = t('v.phone')
  const phoneString = z.string({ error: PHONE_MSG }).regex(/^\+?\d{7,15}$/, PHONE_MSG)
  const phone = z.preprocess((v) => blank(normalizePhone(v)), phoneString)
  // optional داخل schema بعد preprocess حتى تُقبل السلسلة الفارغة بعد تحويلها إلى undefined.
  const optionalPhone = z.preprocess((v) => blank(normalizePhone(v)), phoneString.optional())
  const emailString = z.string({ error: t('v.emailInvalid') }).max(254, t('v.emailLong')).pipe(z.email(t('v.emailFormat')))
  const optionalEmail = z.preprocess((v) => blank(typeof v === 'string' ? v.trim().toLowerCase() : v), emailString.optional())

  // حقول مشتركة: فخ الروبوتات + رمز النموذج + لغة الصفحة
  const BODY_ERR = t('v.body')
  const antiBot = {
    website: z.unknown().optional(), // honeypot: يجب أن يبقى فارغًا (أي قيمة = روبوت)
    formToken: z.unknown().optional(), // يُفحص في checkFormToken (نوع/طول/توقيع)
    lang: z.unknown().optional(), // يُقرأ مسبقًا لاختيار المخطط
  }

  const inquiryBody = z
    .object({
      name: text(L('name'), 2, 100),
      phone: optionalPhone,
      email: optionalEmail,
      message: text(L('message'), 5, 2000),
      productId: z.preprocess((v) => (typeof v === 'string' && v.trim() !== '' ? Number(v) : blank(v)), z.number({ error: t('v.product') }).int().positive().max(2_147_483_647).optional()),
      ...antiBot,
    }, { error: BODY_ERR })
    .refine((d) => d.phone || d.email, { path: ['phone'], message: t('v.contact') })

  const distributionBody = z.object({
    name: text(L('name'), 2, 100),
    businessName: text(L('business'), 2, 150),
    phone,
    whatsapp: optionalPhone,
    email: optionalEmail,
    city: optText(L('city'), 100),
    area: optText(L('area'), 100),
    businessType: z.enum(BUSINESS_TYPES, { error: t('v.type') }),
    message: optText(L('message'), 2000),
    ...antiBot,
  }, { error: BODY_ERR })

  return { inquiryBody, distributionBody }
}

const cache = {}
export const getFormSchemas = (lang) => (cache[lang] ??= buildFormSchemas(lang))
export const { inquiryBody, distributionBody } = getFormSchemas('ar') // الافتراضي (العربية)

// ---------- لوحة المدير ----------
export const INQUIRY_STATUSES = ['new', 'contacted', 'closed']
export const DISTRIBUTION_STATUSES = ['new', 'contacted', 'qualified', 'closed']

const listQuery = (statuses) =>
  z.object({
    status: z.enum(['all', ...statuses], { error: 'الحالة غير صالحة' }).default('all'),
    search: z.preprocess(blank, z.string().trim().max(100, 'عبارة البحث طويلة').optional()),
    page: z.preprocess(blank, z.coerce.number({ error: 'رقم الصفحة غير صالح' }).int().min(1).max(100000).default(1)),
    pageSize: z.preprocess(blank, z.coerce.number({ error: 'حجم الصفحة غير صالح' }).int().min(1).max(100).default(20)),
  })
export const inquiryListQuery = listQuery(INQUIRY_STATUSES)
export const distributionListQuery = listQuery(DISTRIBUTION_STATUSES)

export const inquiryStatusBody = z.object({ status: z.enum(INQUIRY_STATUSES, { error: 'الحالة غير صالحة' }) }, { error: 'بيانات غير صالحة' })
export const distributionStatusBody = z.object({ status: z.enum(DISTRIBUTION_STATUSES, { error: 'الحالة غير صالحة' }) }, { error: 'بيانات غير صالحة' })
export const idParam = z.object({ id: z.coerce.number({ error: 'المعرّف غير صالح' }).int().min(1).max(2_147_483_647) })
