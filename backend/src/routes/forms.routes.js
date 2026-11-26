import { Router } from 'express'
import { rateLimit } from 'express-rate-limit'
import { fail } from '../utils/apiResponse.js'
import { originGuard } from '../middleware/originGuard.js'
import { token, submitInquiry, submitDistribution } from '../controllers/forms.controller.js'
import { apiT, langOf } from '../i18n/index.js'

// رسالة الحد بلغة الصفحة (حقل lang في الجسم)
const tooMany = (req, res) => fail(res, 429, apiT(langOf(req))('form.tooMany'))

// حدّان على الإرسال المقبول: قصير (5 كل 15 دقيقة) ويومي (20). الطلبات المرفوضة بالتحقق لا تُحتسب.
const burst = rateLimit({ windowMs: 15 * 60 * 1000, limit: 5, skipFailedRequests: true, standardHeaders: 'draft-7', legacyHeaders: false, handler: tooMany })
const daily = rateLimit({ windowMs: 24 * 60 * 60 * 1000, limit: 20, skipFailedRequests: true, standardHeaders: false, legacyHeaders: false, handler: tooMany })

// نماذج الموقع العامة (بدون مصادقة)
const router = Router()
router.get('/forms/token', token)
router.post('/inquiries', originGuard, burst, daily, submitInquiry)
router.post('/distribution-requests', originGuard, burst, daily, submitDistribution)

export default router
