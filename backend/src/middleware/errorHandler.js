import { env } from '../config/env.js'
import { AppError } from '../utils/AppError.js'
import { fail } from '../utils/apiResponse.js'

// آخر middleware: يحوّل أي خطأ إلى JSON موحّد. Express 5 يمرّر أخطاء async إليه تلقائيًا.
// eslint-disable-next-line no-unused-vars
export function errorHandler(err, _req, res, _next) {
  if (err instanceof AppError) {
    return fail(res, err.statusCode, err.message, err.errors)
  }
  // أخطاء الـ body parser (JSON غير صالح / حجم كبير)
  if (err?.type === 'entity.parse.failed') return fail(res, 400, 'صيغة JSON غير صحيحة')
  if (err?.type === 'entity.too.large') return fail(res, 413, 'حجم الطلب كبير جدًا')

  console.error(err) // الخطأ الكامل في السجل فقط
  const message = env.NODE_ENV === 'production' ? 'حدث خطأ غير متوقع في الخادم' : `خطأ في الخادم: ${err?.message}`
  return fail(res, 500, message)
}
