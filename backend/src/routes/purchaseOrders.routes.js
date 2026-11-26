import { Router } from 'express'
import { rateLimit } from 'express-rate-limit'
import { fail } from '../utils/apiResponse.js'
import { originGuard } from '../middleware/originGuard.js'
import { apiT, langOf } from '../i18n/index.js'
import { submitPurchaseOrder } from '../controllers/purchaseOrders.controller.js'

const tooMany = (req, res) => fail(res, 429, apiT(langOf(req))('form.tooMany'))

// Separate limits for purchase orders: short burst and daily cap.
// Failed attempts count here as well, so honeypot/token failures cannot bypass the limit.
const burst = rateLimit({
  windowMs: 15 * 60 * 1000,
  limit: 5,
  standardHeaders: 'draft-7',
  legacyHeaders: false,
  handler: tooMany,
})
const daily = rateLimit({
  windowMs: 24 * 60 * 60 * 1000,
  limit: 20,
  standardHeaders: false,
  legacyHeaders: false,
  handler: tooMany,
})

const router = Router()
router.post('/purchase-orders', originGuard, burst, daily, submitPurchaseOrder)

export default router
