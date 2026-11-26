import { env } from '../config/env.js'
import { fail } from '../utils/apiResponse.js'

const SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS'])

// حماية CSRF الطبقة الثانية (الأولى: SameSite=Strict على الكوكي).
// أي طلب يغيّر البيانات يجب أن يحمل Origin يطابق مضيف الخادم (أو أحد CORS_ORIGINS).
export function originGuard(req, res, next) {
  if (SAFE_METHODS.has(req.method)) return next()
  const origin = req.get('origin')
  if (!origin) return fail(res, 403, 'طلب مرفوض: المصدر غير معروف')
  let host
  try {
    host = new URL(origin).host
  } catch {
    return fail(res, 403, 'طلب مرفوض: مصدر غير صالح')
  }
  if (host === req.host || env.CORS_ORIGINS.includes(origin)) return next()
  return fail(res, 403, 'طلب مرفوض: مصدر غير مسموح')
}
