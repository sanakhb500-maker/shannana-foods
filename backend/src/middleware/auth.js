import { fail } from '../utils/apiResponse.js'
import { readSessionUser } from '../services/auth.service.js'

// يتطلب جلسة admin صالحة. يضع المستخدم في req.admin.
export async function requireAdmin(req, res, next) {
  const user = await readSessionUser(req)
  if (!user) return fail(res, 401, 'يلزم تسجيل الدخول')
  req.admin = user
  next()
}

// بيانات حساسة: لا تُخزَّن في أي كاش.
export function noStore(_req, res, next) {
  res.set('Cache-Control', 'no-store')
  next()
}
