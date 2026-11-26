import { created, ok, fail } from '../utils/apiResponse.js'
import { getFormSchemas } from '../validators/forms.validators.js'
import { apiT, langOf } from '../i18n/index.js'
import { issueFormToken, checkFormToken, looksLikeSpam, MIN_FILL_MS } from '../utils/formGuards.js'
import * as svc from '../services/forms.service.js'

const errorsOf = (issues) => issues.map((i) => ({ field: i.path.join('.') || 'body', message: i.message }))
const SUCCESS_KEY = { inquiry: 'form.okInquiry', distribution: 'form.okDistribution' }

export function token(_req, res) {
  res.set('Cache-Control', 'no-store')
  return ok(res, { token: issueFormToken() })
}

// الإرسال الآلي (honeypot ممتلئ، أو أسرع من زمن بشري، أو روابط مزعجة) يحصل على نجاح شكلي ولا يُحفظ.
function isBot(d, tokenState) {
  return Boolean(d.website) || tokenState.ageMs < MIN_FILL_MS || looksLikeSpam(d.message)
}

// الرسائل (نجاح/أخطاء) بلغة الصفحة: الحقل lang في الجسم ('ar' افتراضيًا، 'en' للصفحة الإنجليزية)
async function handle(req, res, schemaOf, kind, save) {
  const lang = langOf(req)
  const t = apiT(lang)
  const parsed = schemaOf(getFormSchemas(lang)).safeParse(req.body ?? {})
  if (!parsed.success) return fail(res, 400, t('form.fixFields'), errorsOf(parsed.error.issues))
  const d = parsed.data
  const tok = checkFormToken(d.formToken)
  if (!tok.valid) return fail(res, 400, tok.expired ? t('form.expired') : t('form.genericFail'))
  if (isBot(d, tok)) return created(res, { message: t(SUCCESS_KEY[kind]) })
  const extra = await save(d, t)
  if (extra) return extra
  return created(res, { message: t(SUCCESS_KEY[kind]) })
}

export const submitInquiry = (req, res) =>
  handle(req, res, (s) => s.inquiryBody, 'inquiry', async (d, t) => {
    if (d.productId && !(await svc.findActiveProduct(d.productId))) return fail(res, 400, t('form.fixFields'), [{ field: 'productId', message: t('form.productUnavailable') }])
    await svc.createInquiry(d) // التكرار الحديث يُقبل بصمت (نجاح بدون سجل ثانٍ)
    return null
  })

export const submitDistribution = (req, res) =>
  handle(req, res, (s) => s.distributionBody, 'distribution', async (d) => {
    await svc.createDistributionRequest(d)
    return null
  })
