// منطق المصادقة: التحقق من كلمة المرور (Argon2id) + جلسة JWT داخل HttpOnly cookie.
import crypto from 'node:crypto'
import argon2 from 'argon2'
import jwt from 'jsonwebtoken'
import { env } from '../config/env.js'
import { prisma } from '../config/prisma.js'

const isProd = env.NODE_ENV === 'production'

// في الإنتاج: بادئة __Host- تفرض Secure + Path=/ وبدون Domain.
export const COOKIE_NAME = isProd ? '__Host-sf_admin' : 'sf_admin'
export const SESSION_SECONDS = 8 * 60 * 60
const ISSUER = 'shannana-foods'
const AUDIENCE = 'admin'
const ARGON_OPTIONS = { type: argon2.argon2id, memoryCost: 65536, timeCost: 3, parallelism: 1 }

const cookieBase = { httpOnly: true, secure: isProd, sameSite: 'strict', path: '/' }

// hash وهمي: يُستعمل عند عدم وجود المستخدم كي يتساوى زمن الرد تقريبًا (لا يكشف وجود البريد).
const DUMMY_HASH = await argon2.hash(crypto.randomBytes(24).toString('hex'), ARGON_OPTIONS)

// بصمة قصيرة من hash كلمة المرور: تغيير كلمة المرور يُبطل كل الجلسات القديمة.
const fingerprint = (passwordHash) => crypto.createHash('sha256').update(passwordHash).digest('hex').slice(0, 16)

export const publicUser = (u) => ({ id: u.id, name: u.name, email: u.email, role: u.role })

export async function verifyCredentials(email, password) {
  const user = await prisma.user.findUnique({ where: { email } })
  let valid = false
  try {
    valid = await argon2.verify(user?.passwordHash ?? DUMMY_HASH, password)
  } catch {
    valid = false
  }
  if (!user || !valid || !user.isActive || user.role !== 'admin') return null
  return user
}

export function signSession(user) {
  return jwt.sign({ role: user.role, pv: fingerprint(user.passwordHash) }, env.JWT_SECRET, {
    algorithm: 'HS256',
    subject: String(user.id),
    issuer: ISSUER,
    audience: AUDIENCE,
    expiresIn: SESSION_SECONDS,
  })
}

// يعيد المستخدم (admin نشط) أو null. يقرأ القاعدة في كل طلب، فتعطيل الحساب يسري فورًا.
export async function userFromToken(token) {
  if (!token || typeof token !== 'string') return null
  let payload
  try {
    payload = jwt.verify(token, env.JWT_SECRET, { algorithms: ['HS256'], issuer: ISSUER, audience: AUDIENCE })
  } catch {
    return null
  }
  const id = Number(payload.sub)
  if (!Number.isInteger(id)) return null
  const user = await prisma.user.findUnique({ where: { id } })
  if (!user || !user.isActive || user.role !== 'admin') return null
  if (payload.pv !== fingerprint(user.passwordHash)) return null
  return user
}

export const readSessionUser = (req) => userFromToken(req.cookies?.[COOKIE_NAME])

export function setSessionCookie(res, token) {
  res.cookie(COOKIE_NAME, token, { ...cookieBase, maxAge: SESSION_SECONDS * 1000 })
}

export function clearSessionCookie(res) {
  res.clearCookie(COOKIE_NAME, cookieBase)
}
