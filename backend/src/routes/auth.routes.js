import { Router } from 'express'
import { rateLimit } from 'express-rate-limit'
import { fail } from '../utils/apiResponse.js'
import { originGuard } from '../middleware/originGuard.js'
import { requireAdmin, noStore } from '../middleware/auth.js'
import { login, logout, me } from '../controllers/auth.controller.js'

const tooMany = (_req, res) => fail(res, 429, 'محاولات كثيرة، يرجى المحاولة بعد 15 دقيقة')

// المحاولات الفاشلة فقط تُحتسب (الناجحة لا تستهلك الحد).
const perIp = rateLimit({ windowMs: 15 * 60 * 1000, limit: 10, skipSuccessfulRequests: true, standardHeaders: 'draft-7', legacyHeaders: false, handler: tooMany })
const perEmail = rateLimit({
  windowMs: 15 * 60 * 1000,
  limit: 30,
  skipSuccessfulRequests: true,
  standardHeaders: false,
  legacyHeaders: false,
  keyGenerator: (req) => `email:${String(req.body?.email ?? '').trim().toLowerCase().slice(0, 254)}`,
  validate: { keyGeneratorIpFallback: false },
  handler: tooMany,
})

const router = Router()
router.use(noStore, originGuard)
router.post('/login', perIp, perEmail, login)
router.post('/logout', logout)
router.get('/me', requireAdmin, me)

export default router
