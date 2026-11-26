import { created, fail } from '../utils/apiResponse.js'
import { apiT, langOf } from '../i18n/index.js'
import { env } from '../config/env.js'
import { checkFormToken, looksLikeSpam, MIN_FILL_MS } from '../utils/formGuards.js'
import { getPurchaseOrderSchema } from '../validators/purchaseOrders.validators.js'
import { createPurchaseOrder, PurchaseOrderValidationError } from '../services/purchaseOrders.service.js'

const errorsOf = (issues) => issues.map((issue) => ({ field: issue.path.join('.') || 'body', message: issue.message }))

const businessErrorMessage = (code, lang) => {
  const messages = {
    PRODUCT_UNAVAILABLE: {
      ar: 'أحد المنتجات في السلة لم يعد متاحًا. حدّثي الصفحة وراجعي السلة.',
      en: 'One or more products in the cart are no longer available. Refresh the page and review your cart.',
    },
    CARTON_NOT_AVAILABLE: {
      ar: 'لا يمكن طلب كراتين لهذا المنتج لأن عدد الوحدات في الكرتونة غير محدد.',
      en: 'Cartons cannot be ordered for this product because its units-per-carton value is not configured.',
    },
    INVALID_TOTAL_QUANTITY: {
      ar: 'إجمالي كمية أحد المنتجات غير صالح. راجعي الكميات وحاولي مجددًا.',
      en: 'The total quantity for one of the products is invalid. Review the quantities and try again.',
    },
  }
  return messages[code]?.[lang === 'en' ? 'en' : 'ar'] ?? (lang === 'en' ? 'The order could not be validated.' : 'تعذر التحقق من الطلب.')
}

const oneLine = (value) => String(value ?? '').replace(/[\r\n]+/g, ' ').replace(/\s+/g, ' ').trim()
const qty = (value) => new Intl.NumberFormat('en-US').format(Number(value) || 0)

function quantityText(item, en) {
  const cartons = Number(item.cartonQuantity) || 0
  const units = Number(item.unitQuantity) || 0
  const total = Number(item.totalUnits) || 0

  if (cartons > 0) {
    const cartonLabel = en ? (cartons === 1 ? 'carton' : 'cartons') : (cartons === 1 ? 'كرتون' : 'كراتين')
    const unitLabel = en ? (Number(item.unitsPerCarton) === 1 ? 'unit' : 'units') : 'وحدة'
    let text = `${qty(cartons)} ${cartonLabel} × ${qty(item.unitsPerCarton)} ${unitLabel}`
    if (units > 0) {
      const individualLabel = en ? (units === 1 ? 'unit' : 'units') : (units === 1 ? 'وحدة' : 'وحدات')
      const totalLabel = en ? (total === 1 ? 'unit' : 'units') : (total === 1 ? 'وحدة' : 'وحدات')
      text += ` + ${qty(units)} ${individualLabel} (${qty(total)} ${totalLabel} ${en ? 'total' : 'إجمالاً'})`
    }
    return text
  }

  const unitLabel = en ? (units === 1 ? 'unit' : 'units') : (units === 1 ? 'وحدة' : 'وحدات')
  return `${qty(units)} ${unitLabel}`
}

function buildOrderWhatsAppMessage({ lang, reference, customer, items }) {
  const en = lang === 'en'
  const totalUnits = items.reduce((sum, item) => sum + Number(item.totalUnits || 0), 0)
  const lines = en
    ? [
        '*SHANNANA FOODS | NEW PURCHASE ORDER*',
        `*Order reference:* ${reference}`,
        '',
        '*CUSTOMER DETAILS*',
        `• Contact person: ${oneLine(customer.customerName)}`,
        `• Business: ${oneLine(customer.businessName)}`,
        `• Phone: ${oneLine(customer.phone)}`,
        `• City: ${oneLine(customer.city)}`,
      ]
    : [
        '*شنانة فودز | طلب شراء جديد*',
        `*رقم الطلب:* ${reference}`,
        '',
        '*بيانات العميل*',
        `• الاسم: ${oneLine(customer.customerName)}`,
        `• المنشأة: ${oneLine(customer.businessName)}`,
        `• الهاتف: ${oneLine(customer.phone)}`,
        `• المدينة: ${oneLine(customer.city)}`,
      ]

  const optionalFields = en
    ? [
        ['Area', customer.area],
        ['Address', customer.address],
        ['Email', customer.email],
        ['Additional WhatsApp', customer.whatsapp],
      ]
    : [
        ['المنطقة', customer.area],
        ['العنوان', customer.address],
        ['البريد الإلكتروني', customer.email],
        ['واتساب إضافي', customer.whatsapp],
      ]
  for (const [label, value] of optionalFields) {
    const clean = oneLine(value)
    if (clean) lines.push(`• ${label}: ${clean}`)
  }

  lines.push('', en ? `*ORDER ITEMS (${items.length})*` : `*المنتجات المطلوبة (${qty(items.length)})*`)
  items.forEach((item, index) => {
    const name = oneLine(en ? (item.productNameEn || item.productName) : item.productName)
    lines.push(`${index + 1}. ${name} — ${quantityText(item, en)}`)
  })

  lines.push(en
    ? `\n*TOTAL QUANTITY: ${qty(totalUnits)} ${totalUnits === 1 ? 'unit' : 'units'}*`
    : `\n*إجمالي الطلب: ${qty(totalUnits)} ${totalUnits === 1 ? 'وحدة' : 'وحدات'}*`)

  const note = oneLine(customer.customerNote)
  if (note) {
    lines.push('', en ? '*CUSTOMER NOTE*' : '*ملاحظات العميل*', note)
  }

  lines.push(
    '',
    en
      ? '*ACTION REQUIRED:* Review product availability and contact the customer to confirm pricing and details.'
      : '*المطلوب:* مراجعة توفر المنتجات والتواصل مع العميل لتأكيد السعر والتفاصيل.',
    en
      ? '*Status:* New — awaiting review. The order is not confirmed yet.'
      : '*حالة الطلب:* جديد — بانتظار المراجعة، ولم يتم تأكيده بعد.',
  )

  return lines.join('\n')
}

function buildWhatsAppUrl(message) {
  return `https://wa.me/${env.ORDER_WHATSAPP_NUMBER}?text=${encodeURIComponent(message)}`
}

export async function submitPurchaseOrder(req, res) {
  const lang = langOf(req)
  const t = apiT(lang)
  const parsed = getPurchaseOrderSchema(lang).safeParse(req.body ?? {})
  if (!parsed.success) return fail(res, 400, t('form.fixFields'), errorsOf(parsed.error.issues))

  const data = parsed.data
  const tokenState = checkFormToken(data.formToken)
  if (!tokenState.valid) return fail(res, 400, tokenState.expired ? t('form.expired') : t('form.genericFail'))

  // For purchase orders, never return a false success for bot-like submissions:
  // the storefront must only clear its cart after a real database save.
  const botLike = Boolean(data.website) || tokenState.ageMs < MIN_FILL_MS || looksLikeSpam(data.customerNote)
  if (botLike) return fail(res, 400, t('form.genericFail'))

  try {
    // WhatsApp is prepared only after the database transaction has committed.
    // Opening the link is a separate customer action; it does not change order status.
    const order = await createPurchaseOrder(data)
    const reference = `SF-${String(order.id).padStart(6, '0')}`
    const whatsappMessage = buildOrderWhatsAppMessage({ lang, reference, customer: data, items: order.items })
    const whatsappUrl = buildWhatsAppUrl(whatsappMessage)

    return created(res, {
      message: lang === 'en' ? 'Your purchase order has been received.' : 'تم استلام طلب الشراء بنجاح.',
      order: { reference, status: order.status, createdAt: order.createdAt, whatsappUrl },
    })
  } catch (error) {
    if (error instanceof PurchaseOrderValidationError) {
      return fail(res, 400, t('form.fixFields'), [{ field: error.field, message: businessErrorMessage(error.code, lang) }])
    }
    throw error
  }
}
