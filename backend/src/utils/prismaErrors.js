// يحوّل أخطاء Prisma الشائعة إلى {status, message, field?}. يعيد null لأي خطأ آخر.
const SPEC_CHECK_MESSAGES = {
  products_net_content_positive_chk: ['netContent', 'المحتوى الصافي يجب أن يكون أكبر من صفر'],
  products_net_content_unit_pair_chk: ['netContentUnit', 'أدخل المحتوى الصافي ووحدته معًا، أو اتركهما فارغين'],
  products_barcode_format_chk: ['barcode', 'الباركود يجب أن يتكون من 8 أو 12 إلى 14 رقمًا'],
  products_pack_texts_not_blank_chk: ['packType', 'لا تترك نصوص نوع العبوة أو مادتها فارغة؛ اترك الحقل فارغًا بالكامل إن لم تتوفر المعلومة'],
  products_carton_units_positive_chk: ['cartonUnits', 'عدد العبوات في الكرتونة يجب أن يكون أكبر من صفر'],
  products_carton_dims_positive_chk: ['cartonLengthCm', 'أبعاد الكرتونة يجب أن تكون أكبر من صفر'],
  products_carton_dims_all_or_none_chk: ['cartonLengthCm', 'أدخل الطول والعرض والارتفاع معًا أو اتركها جميعًا فارغة'],
  products_carton_weights_positive_chk: ['cartonNetWeightKg', 'أوزان الكرتونة يجب أن تكون أكبر من صفر'],
  products_carton_gross_ge_net_chk: ['cartonGrossWeightKg', 'الوزن الإجمالي يجب ألا يقل عن الوزن الصافي'],
  products_shelf_life_positive_chk: ['shelfLifeValue', 'مدة الصلاحية يجب أن تكون أكبر من صفر'],
  products_shelf_life_pair_chk: ['shelfLifeUnit', 'أدخل مدة الصلاحية ووحدتها معًا، أو اتركهما فارغين'],
  products_notes_not_blank_chk: ['storageNote', 'لا تترك ملاحظات التخزين أو التصدير بمسافات فقط'],
  products_origin_country_format_chk: ['originCountry', 'رمز بلد المنشأ يجب أن يتكون من حرفين إنجليزيين كبيرين'],
  products_hs_code_format_chk: ['hsCode', 'رمز HS يجب أن يتكون من 6 إلى 10 أرقام'],
  products_pallet_positive_chk: ['palletCartons', 'قيم الطبلية يجب أن تكون أعدادًا صحيحة أكبر من صفر'],
  products_pallet_layer_le_total_chk: ['palletCartonsPerLayer', 'عدد الكراتين في الطبقة لا يمكن أن يتجاوز إجمالي كراتين الطبلية'],
}

function errorText(err) {
  let meta = ''
  try { meta = JSON.stringify(err?.meta ?? {}) } catch { /* ignore malformed adapter metadata */ }
  return `${meta} ${err?.message ?? ''}`
}

function checkConstraint(err) {
  const hay = errorText(err)
  const match = /constraint\s+["'`]([^"'`]+)["'`]/i.exec(hay)
  if (match?.[1]) return match[1]
  return Object.keys(SPEC_CHECK_MESSAGES).find((name) => hay.includes(name)) ?? null
}

export function mapPrismaError(err, labels = {}) {
  if (err?.code === 'P2002') {
    // Prisma 7 + adapter-pg may expose the column or the database index name.
    const target = Array.isArray(err.meta?.target) ? err.meta.target.join(',') : String(err.meta?.target ?? '')
    const hay = `${target} ${errorText(err)}`.toLowerCase()
    const field = hay.includes('barcode') ? 'barcode' : /slug/.test(hay) ? 'slug' : /(^|[^a-z])name([^a-z]|$)/.test(hay) ? 'name' : undefined
    return { status: 409, field, message: (field && labels[field]) || (field === 'barcode' ? 'هذا الباركود مستخدم لمنتج آخر' : 'القيمة مستخدمة مسبقًا') }
  }
  if (err?.code === 'P2025') return { status: 404, message: 'العنصر غير موجود' }
  if (err?.code === 'P2003') return { status: 409, message: 'العنصر مرتبط ببيانات أخرى' }

  // PostgreSQL CHECK violations can surface as P2004 or as P2039 with Prisma 7 driver adapters.
  if (err?.code === 'P2004' || err?.code === 'P2039') {
    const constraint = checkConstraint(err)
    if (constraint && SPEC_CHECK_MESSAGES[constraint]) {
      const [field, message] = SPEC_CHECK_MESSAGES[constraint]
      return { status: 400, field, message }
    }
    const hay = errorText(err)
    if (/23514|check constraint/i.test(hay)) return { status: 400, message: 'بعض مواصفات المنتج لا تتوافق مع شروط قاعدة البيانات؛ راجع القيم المدخلة' }
    if (/23505|unique constraint/i.test(hay)) return { status: 409, field: 'barcode', message: labels.barcode || 'هذا الباركود مستخدم لمنتج آخر' }
  }

  if (err?.code === 'P2000') return { status: 400, message: 'قيمة أحد الحقول أطول من الحد المسموح' }
  if (err?.code === 'P2006' || err?.code === 'P2007' || err?.code === 'P2020' || err?.code === 'P2010') {
    return { status: 400, message: 'إحدى القيم غير صالحة أو خارج النطاق المسموح' }
  }
  return null
}
