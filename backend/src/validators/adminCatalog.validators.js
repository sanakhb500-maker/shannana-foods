import { z } from 'zod'

// Multipart values arrive as strings. Empty optional values become NULL.
const toBool = (v) => (v === 'true' || v === '1' || v === 'on' ? true : v === 'false' || v === '0' || v === 'off' ? false : v)
const bool = (msg) => z.preprocess(toBool, z.boolean({ error: msg }))
const int = (min, max, msg) =>
  z.preprocess((v) => (typeof v === 'string' && v.trim() !== '' ? Number(v) : v), z.number({ error: msg }).int(msg).min(min, msg).max(max, msg))
const blankToUndefined = (v) => (typeof v === 'string' && v.trim() === '' ? undefined : v)
const blankToNull = (v) => (typeof v === 'string' && v.trim() === '' ? null : v)
const normalizeOptionalText = (v) => (typeof v === 'string' ? (v.trim() === '' ? null : v.trim()) : v)

const SLUG_RE = /^[a-z0-9]+(?:-[a-z0-9]+)*$/
const slug = z.preprocess(
  blankToUndefined,
  z.string({ error: 'المعرّف غير صالح' }).trim().toLowerCase().max(100, 'المعرّف طويل جدًا').regex(SLUG_RE, 'المعرّف: أحرف إنجليزية صغيرة وأرقام وشرطات فقط (مثل tomato-paste)'),
)
const description = z
  .string({ error: 'الوصف غير صالح' })
  .trim()
  .max(2000, 'الوصف طويل جدًا (الحد 2000 حرف)')
  .transform((s) => (s === '' ? null : s))
const id = int(1, 2_147_483_647, 'المعرّف غير صالح')
const optNullText = (label, max) =>
  z.preprocess(normalizeOptionalText, z.string({ error: `${label} غير صالح` }).max(max, `${label} طويل جدًا (الحد ${max} حرف)`).nullable()).optional()

export const TONES = ['red', 'gold', 'rose', 'green']

// Keep exact decimal input as a string. Do not convert measurements to JS Number.
function decimalText(label, maxIntegerDigits, maxScale) {
  return z.preprocess(
    (v) => {
      if (typeof v === 'number' && Number.isFinite(v)) return String(v)
      if (typeof v === 'string') return v.trim() === '' ? null : v.trim()
      return v
    },
    z.string({ error: `${label} غير صالح` })
      .max(maxIntegerDigits + maxScale + 2, `${label} طويل جدًا`)
      .regex(/^\d+(?:\.\d+)?$/, `${label}: أدخل رقمًا عشريًا موجبًا باستخدام النقطة`)
      .refine((value) => {
        const [whole, fraction = ''] = value.split('.')
        const significantWholeDigits = whole.replace(/^0+/, '').length || 1
        return significantWholeDigits <= maxIntegerDigits && fraction.length <= maxScale
      }, `${label}: الحد ${maxIntegerDigits} أرقام قبل الفاصلة و${maxScale} بعدها`)
      .refine((value) => Number(value) > 0, `${label} يجب أن يكون أكبر من صفر`)
      .nullable(),
  ).optional()
}

function positiveIntOptional(label, max = 2_147_483_647) {
  return z.preprocess(
    (v) => {
      if (typeof v === 'string') {
        const s = v.trim()
        if (!s) return null
        if (/^\d+$/.test(s)) return Number(s)
      }
      return v
    },
    z.number({ error: `${label} غير صالح` }).int(`${label} يجب أن يكون عددًا صحيحًا`).min(1, `${label} يجب أن يكون أكبر من صفر`).max(max, `${label} أكبر من الحد المسموح`).nullable(),
  ).optional()
}

function nullableEnum(values, label) {
  return z.preprocess(blankToNull, z.enum(values, { error: `${label} غير صالح` }).nullable()).optional()
}

const barcode = z.preprocess(
  normalizeOptionalText,
  z.string({ error: 'الباركود غير صالح' }).max(14, 'الباركود لا يتجاوز 14 رقمًا').regex(/^(?:\d{8}|\d{12,14})$/, 'الباركود يجب أن يتكون من 8 أو 12 إلى 14 رقمًا').nullable(),
).optional()

const originCountry = z.preprocess(
  (v) => typeof v === 'string' ? (v.trim() === '' ? null : v.trim().toUpperCase()) : v,
  z.string({ error: 'رمز بلد المنشأ غير صالح' }).regex(/^[A-Z]{2}$/, 'أدخل رمز بلد المنشأ بحرفين إنجليزيين كبيرين (مثل SY)').nullable(),
).optional()

const hsCode = z.preprocess(
  normalizeOptionalText,
  z.string({ error: 'رمز HS غير صالح' }).max(10, 'رمز HS لا يتجاوز 10 أرقام').regex(/^\d{6,10}$/, 'رمز HS يجب أن يتكون من 6 إلى 10 أرقام').nullable(),
).optional()

const specificationFields = {
  // العبوة
  netContent: decimalText('المحتوى الصافي', 7, 3),
  netContentUnit: nullableEnum(['g', 'kg', 'ml', 'l'], 'وحدة المحتوى الصافي'),
  packType: optNullText('نوع العبوة بالعربية', 80),
  packTypeEn: optNullText('نوع العبوة بالإنجليزية', 80),
  packMaterial: optNullText('مادة العبوة بالعربية', 80),
  packMaterialEn: optNullText('مادة العبوة بالإنجليزية', 80),
  barcode,
  // الكرتونة
  cartonUnits: positiveIntOptional('عدد العبوات في الكرتونة'),
  cartonLengthCm: decimalText('طول الكرتونة', 5, 2),
  cartonWidthCm: decimalText('عرض الكرتونة', 5, 2),
  cartonHeightCm: decimalText('ارتفاع الكرتونة', 5, 2),
  cartonNetWeightKg: decimalText('الوزن الصافي للكرتونة', 5, 3),
  cartonGrossWeightKg: decimalText('الوزن الإجمالي للكرتونة', 5, 3),
  // الصلاحية والتخزين
  shelfLifeValue: positiveIntOptional('مدة الصلاحية'),
  shelfLifeUnit: nullableEnum(['day', 'month', 'year'], 'وحدة مدة الصلاحية'),
  storageNote: optNullText('تعليمات التخزين بالعربية', 2000),
  storageNoteEn: optNullText('تعليمات التخزين بالإنجليزية', 2000),
  // التصدير واللوجستيات
  originCountry,
  hsCode,
  palletCartons: positiveIntOptional('عدد الكراتين على الطبلية'),
  palletCartonsPerLayer: positiveIntOptional('عدد الكراتين في الطبقة'),
  exportNote: optNullText('ملاحظات التصدير بالعربية', 2000),
  exportNoteEn: optNullText('ملاحظات التصدير بالإنجليزية', 2000),
}

function scaledDecimal(value, scale) {
  const [whole, fraction = ''] = value.split('.')
  return BigInt(whole) * (10n ** BigInt(scale)) + BigInt((fraction + '0'.repeat(scale)).slice(0, scale) || '0')
}

function validateSpecificationPairs(data, ctx) {
  const pair = (a, b, message) => {
    if (data[a] === undefined && data[b] === undefined) return
    const aSet = data[a] !== undefined && data[a] !== null
    const bSet = data[b] !== undefined && data[b] !== null
    if (aSet !== bSet) {
      const missing = aSet ? b : a
      ctx.addIssue({ code: 'custom', path: [missing], message })
    }
  }

  pair('netContent', 'netContentUnit', 'أدخل المحتوى الصافي ووحدته معًا، أو اتركهما فارغين')
  pair('shelfLifeValue', 'shelfLifeUnit', 'أدخل مدة الصلاحية ووحدتها معًا، أو اتركهما فارغين')

  const dimensions = ['cartonLengthCm', 'cartonWidthCm', 'cartonHeightCm']
  if (dimensions.some((key) => data[key] !== undefined)) {
    const filled = dimensions.filter((key) => data[key] !== undefined && data[key] !== null)
    if (filled.length !== 0 && filled.length !== 3) {
      for (const key of dimensions.filter((item) => !filled.includes(item))) {
        ctx.addIssue({ code: 'custom', path: [key], message: 'أدخل أبعاد الكرتونة الثلاثة معًا أو اتركها جميعًا فارغة' })
      }
    }
  }

  if (data.cartonNetWeightKg != null && data.cartonGrossWeightKg != null && scaledDecimal(data.cartonGrossWeightKg, 3) < scaledDecimal(data.cartonNetWeightKg, 3)) {
    ctx.addIssue({ code: 'custom', path: ['cartonGrossWeightKg'], message: 'الوزن الإجمالي يجب ألا يقل عن الوزن الصافي' })
  }

  if (data.palletCartons != null && data.palletCartonsPerLayer != null && data.palletCartonsPerLayer > data.palletCartons) {
    ctx.addIssue({ code: 'custom', path: ['palletCartonsPerLayer'], message: 'عدد الكراتين في الطبقة لا يمكن أن يتجاوز إجمالي كراتين الطبلية' })
  }
}

const productFields = {
  name: z.string({ error: 'الاسم مطلوب' }).trim().min(1, 'الاسم مطلوب').max(150, 'الاسم طويل جدًا (الحد 150 حرفًا)'),
  nameEn: optNullText('الاسم الإنجليزي', 150),
  descriptionEn: optNullText('الوصف الإنجليزي', 2000),
  description,
  categoryId: int(1, 2_147_483_647, 'اختر التصنيف'),
  tone: z.enum(TONES, { error: 'اللون غير صالح' }),
  sortOrder: int(0, 1_000_000, 'الترتيب رقم بين 0 و1000000'),
  isActive: bool('حالة التفعيل غير صالحة'),
  isFeatured: bool('قيمة التمييز غير صالحة'),
}

const createShape = {
  name: productFields.name,
  nameEn: productFields.nameEn.optional(),
  descriptionEn: productFields.descriptionEn.optional(),
  slug: slug.optional(),
  description: productFields.description.optional(),
  categoryId: productFields.categoryId,
  tone: productFields.tone.optional(),
  sortOrder: productFields.sortOrder.optional(),
  isActive: productFields.isActive.optional(),
  isFeatured: productFields.isFeatured.optional(),
  imageUrl: z.string().max(200).optional(), // مسار صورة موجودة مسبقًا (بديل لرفع ملف)
  ...specificationFields,
}

const updateShape = {
  name: productFields.name.optional(),
  nameEn: productFields.nameEn.optional(),
  descriptionEn: productFields.descriptionEn.optional(),
  description: productFields.description.optional(),
  categoryId: productFields.categoryId.optional(),
  tone: productFields.tone.optional(),
  sortOrder: productFields.sortOrder.optional(),
  isActive: productFields.isActive.optional(),
  isFeatured: productFields.isFeatured.optional(),
  ...specificationFields,
} // slug لا يتغير بعد الإنشاء

export const createProductBody = z.object(createShape).superRefine(validateSpecificationPairs)
export const updateProductBody = z.object(updateShape).superRefine(validateSpecificationPairs)

export const productListQuery = z.object({
  search: z.string().trim().max(100).optional(),
  category: z.preprocess(blankToUndefined, id.optional()),
  status: z.enum(['all', 'active', 'inactive']).default('all'),
  limit: z.preprocess(blankToUndefined, int(1, 500, 'limit غير صالح').optional()),
})

// ---------- التصنيفات ----------
const categoryName = z.string({ error: 'الاسم مطلوب' }).trim().min(1, 'الاسم مطلوب').max(100, 'الاسم طويل جدًا (الحد 100 حرف)')

export const createCategoryBody = z.object({
  name: categoryName,
  nameEn: optNullText('الاسم الإنجليزي', 100).optional(),
  descriptionEn: optNullText('الوصف الإنجليزي', 2000).optional(),
  slug,
  description: description.optional(),
  sortOrder: productFields.sortOrder.optional(),
  isActive: productFields.isActive.optional(),
})

export const updateCategoryBody = z.object({
  name: categoryName.optional(),
  nameEn: optNullText('الاسم الإنجليزي', 100).optional(),
  descriptionEn: optNullText('الوصف الإنجليزي', 2000).optional(),
  description: description.optional(),
  sortOrder: productFields.sortOrder.optional(),
  isActive: productFields.isActive.optional(),
})

export const idParam = z.object({ id })
export const issuesToErrors = (issues) => issues.map((i) => ({ field: i.path.join('.') || 'body', message: i.message }))
