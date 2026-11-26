import { z } from 'zod'
import { cleanText, normalizePhone } from '../utils/formGuards.js'
import { apiT } from '../i18n/index.js'

const blank = (value) => (typeof value === 'string' && value.trim() === '' ? undefined : value)
const cache = new Map()

function buildPurchaseOrderSchema(lang) {
  const t = apiT(lang)
  const labels = lang === 'en'
    ? { name: 'Contact name', business: 'Business name', city: 'City', area: 'Area', address: 'Address', note: 'Note' }
    : { name: 'اسم المسؤول', business: 'اسم المحل أو المنشأة', city: 'المدينة', area: 'المنطقة', address: 'العنوان', note: 'ملاحظات الطلب' }

  const text = (label, min, max) => z.preprocess(
    (value) => (typeof value === 'string' ? cleanText(value) : value),
    z.string({ error: t('v.invalid', { label }) })
      .min(min, min <= 1 ? t('v.required', { label }) : t('v.tooShort', { label, min }))
      .max(max, t('v.tooLong', { label, max })),
  )

  const optionalText = (label, max) => z.preprocess(
    (value) => blank(typeof value === 'string' ? cleanText(value) : value),
    z.string({ error: t('v.invalid', { label }) }).max(max, t('v.tooLongPlain', { label })).optional(),
  )

  const phoneMessage = t('v.phone')
  const phoneString = z.string({ error: phoneMessage }).regex(/^\+?\d{7,15}$/, phoneMessage)
  const phone = z.preprocess((value) => blank(normalizePhone(value)), phoneString)
  const optionalPhone = z.preprocess((value) => blank(normalizePhone(value)), phoneString.optional())
  const emailString = z.string({ error: t('v.emailInvalid') })
    .max(254, t('v.emailLong'))
    .pipe(z.email(t('v.emailFormat')))
  const optionalEmail = z.preprocess(
    (value) => blank(typeof value === 'string' ? value.trim().toLowerCase() : value),
    emailString.optional(),
  )

  const nonNegativeInt = (label) => z.preprocess(
    (value) => (typeof value === 'string' && value.trim() !== '' ? Number(value) : value),
    z.number({ error: `${label}: ${t('v.invalid', { label })}` })
      .int(`${label}: ${t('v.invalid', { label })}`)
      .min(0, `${label}: ${t('v.invalid', { label })}`)
      .max(1_000_000, `${label}: ${t('v.invalid', { label })}`)
      .default(0),
  )

  const itemSchema = z.object({
    productId: z.preprocess(
      (value) => (typeof value === 'string' && value.trim() !== '' ? Number(value) : value),
      z.number({ error: t('v.product') }).int().positive().max(2_147_483_647),
    ),
    cartonQuantity: nonNegativeInt(lang === 'en' ? 'Cartons' : 'عدد الكراتين'),
    unitQuantity: nonNegativeInt(lang === 'en' ? 'Individual units' : 'عدد الوحدات المفردة'),
  }, { error: t('v.body') }).refine(
    (item) => item.cartonQuantity > 0 || item.unitQuantity > 0,
    {
      path: ['unitQuantity'],
      message: lang === 'en' ? 'Choose at least one carton or individual unit.' : 'حددي كمية كراتين أو وحدات مفردة واحدة على الأقل.',
    },
  )

  return z.object({
    customerName: text(labels.name, 2, 100),
    phone,
    businessName: text(labels.business, 2, 150),
    city: text(labels.city, 2, 100),
    area: optionalText(labels.area, 100),
    address: optionalText(labels.address, 300),
    email: optionalEmail,
    whatsapp: optionalPhone,
    customerNote: optionalText(labels.note, 2000),
    items: z.array(itemSchema, { error: t('v.body') })
      .min(1, lang === 'en' ? 'Add at least one product.' : 'أضيفي منتجًا واحدًا على الأقل.')
      .max(50, lang === 'en' ? 'A maximum of 50 different products is allowed per order.' : 'الحد الأقصى 50 منتجًا مختلفًا في الطلب الواحد.'),
    // Shared anti-bot fields; token authenticity is checked in the controller.
    website: z.unknown().optional(),
    formToken: z.unknown().optional(),
    lang: z.unknown().optional(),
  }, { error: t('v.body') }).superRefine((data, ctx) => {
    const seen = new Set()
    data.items.forEach((item, index) => {
      if (seen.has(item.productId)) {
        ctx.addIssue({
          code: 'custom',
          path: ['items', index, 'productId'],
          message: lang === 'en' ? 'This product appears more than once.' : 'هذا المنتج مكرر في الطلب.',
        })
      }
      seen.add(item.productId)
    })
  })
}

export const getPurchaseOrderSchema = (lang = 'ar') => {
  if (!cache.has(lang)) cache.set(lang, buildPurchaseOrderSchema(lang))
  return cache.get(lang)
}

export const purchaseOrderBody = getPurchaseOrderSchema('ar')
