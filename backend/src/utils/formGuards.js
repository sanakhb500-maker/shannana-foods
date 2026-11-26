import crypto from 'node:crypto'
import { env } from '../config/env.js'

// ---------- رمز النموذج (يمنع الإرسال الآلي السريع بدون تحميل الصفحة) ----------
// رمز موقَّع من الخادم: "<وقت الإصدار>.<توقيع HMAC>". لا يعتمد على ساعة المتصفح.
const KEY = crypto.createHmac('sha256', env.JWT_SECRET).update('sf-form-token-v1').digest()
const sign = (ts) => crypto.createHmac('sha256', KEY).update(String(ts)).digest('base64url')

export const MIN_FILL_MS = 2500 // أقل زمن بشري معقول بين فتح النموذج وإرساله
export const MAX_TOKEN_AGE_MS = 24 * 60 * 60 * 1000

export const issueFormToken = () => {
  const ts = Date.now()
  return `${ts}.${sign(ts)}`
}

// يعيد { valid, ageMs }. valid=false لرمز مفقود/معدّل/منتهي.
export function checkFormToken(token) {
  if (typeof token !== 'string' || token.length > 100) return { valid: false }
  const [tsRaw, sig] = token.split('.')
  const ts = Number(tsRaw)
  if (!Number.isInteger(ts) || !sig) return { valid: false }
  const expected = Buffer.from(sign(ts))
  const given = Buffer.from(sig)
  if (expected.length !== given.length || !crypto.timingSafeEqual(expected, given)) return { valid: false }
  const ageMs = Date.now() - ts
  if (ageMs < 0 || ageMs > MAX_TOKEN_AGE_MS) return { valid: false, expired: ageMs > MAX_TOKEN_AGE_MS }
  return { valid: true, ageMs }
}

// ---------- تنظيف النصوص ----------
// يزيل محارف التحكم ومحارف اتجاه النص الخفية (تُستعمل لتزوير العرض)، ويبقي الأسطر الجديدة.
export const cleanText = (s) =>
  String(s)
    .replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F\u200B-\u200F\u202A-\u202E\u2066-\u2069\uFEFF]/g, '')
    .replace(/\r\n?/g, '\n')
    .trim()

// أرقام عربية/فارسية → إنجليزية، وإزالة المسافات والشرطات والأقواس
export function normalizePhone(v) {
  if (typeof v !== 'string') return v
  const ascii = v
    .replace(/[\u0660-\u0669]/g, (d) => String(d.charCodeAt(0) - 0x0660))
    .replace(/[\u06F0-\u06F9]/g, (d) => String(d.charCodeAt(0) - 0x06f0))
  let p = ascii.replace(/[\s\-.()]/g, '')
  if (p.startsWith('00')) p = '+' + p.slice(2)
  return p
}

// رسالة فيها روابط كثيرة = إعلان مزعج غالبًا
export const looksLikeSpam = (text) => ((text ?? '').match(/https?:\/\/|www\./gi) || []).length > 2
