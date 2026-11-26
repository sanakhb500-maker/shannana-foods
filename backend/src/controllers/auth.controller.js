import { ok, fail } from '../utils/apiResponse.js'
import { loginBody } from '../validators/auth.validators.js'
import { verifyCredentials, signSession, setSessionCookie, clearSessionCookie, publicUser } from '../services/auth.service.js'

export async function login(req, res) {
  const parsed = loginBody.safeParse(req.body)
  if (!parsed.success) {
    const errors = parsed.error.issues.map((i) => ({ field: i.path.join('.'), message: i.message }))
    return fail(res, 400, 'بيانات غير صحيحة', errors)
  }
  const user = await verifyCredentials(parsed.data.email, parsed.data.password)
  // رسالة واحدة لكل الحالات (بريد غير موجود / كلمة مرور خاطئة / حساب معطّل)
  if (!user) return fail(res, 401, 'البريد الإلكتروني أو كلمة المرور غير صحيحة')
  setSessionCookie(res, signSession(user)) // الرمز في الكوكي فقط، لا يُعاد في الجسم
  return ok(res, { user: publicUser(user) })
}

export function logout(_req, res) {
  clearSessionCookie(res)
  return ok(res, { loggedOut: true })
}

export function me(req, res) {
  return ok(res, { user: publicUser(req.admin) })
}
