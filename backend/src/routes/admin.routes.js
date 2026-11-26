import { Router } from 'express'
import { rateLimit } from 'express-rate-limit'
import { fail } from '../utils/apiResponse.js'
import { originGuard } from '../middleware/originGuard.js'
import { requireAdmin, noStore } from '../middleware/auth.js'
import { stats } from '../controllers/admin.controller.js'
import * as products from '../controllers/adminProducts.controller.js'
import { adminProductTechnicalSheet } from '../controllers/technicalSheet.controller.js'
import * as categories from '../controllers/adminCategories.controller.js'
import * as forms from '../controllers/adminForms.controller.js'
import * as purchaseOrders from '../controllers/adminPurchaseOrders.controller.js'

// كل ما تحت /api/admin يتطلب جلسة admin + فحص Origin للطلبات غير الآمنة.
const router = Router()
router.use(noStore, originGuard, requireAdmin)

// حد إضافي لعمليات الكتابة (يحمي من إساءة استعمال جلسة مسروقة / رفع متكرر)
const writeLimiter = rateLimit({
  windowMs: 10 * 60 * 1000,
  limit: 120,
  skip: (req) => req.method === 'GET',
  standardHeaders: 'draft-7',
  legacyHeaders: false,
  handler: (_req, res) => fail(res, 429, 'عمليات كثيرة، يرجى المحاولة بعد قليل'),
})
router.use(writeLimiter)

router.get('/stats', stats)

router.get('/products', products.list)
router.post('/products', products.create)
router.get('/products/:id/technical-sheet.pdf', adminProductTechnicalSheet)
router.get('/products/:id', products.getOne)
router.patch('/products/:id', products.update)

router.get('/categories', categories.list)
router.post('/categories', categories.create)
router.patch('/categories/:id', categories.update)
router.delete('/categories/:id', categories.remove)

// الاستفسارات وطلبات التوزيع (قراءة + تغيير الحالة فقط)
router.get('/inquiries', forms.listInquiries)
router.patch('/inquiries/:id', forms.setInquiryStatus)
router.get('/distribution-requests', forms.listDistribution)
router.patch('/distribution-requests/:id', forms.setDistributionStatus)

// طلبات الشراء: قراءة القائمة والتفاصيل وتحديث الحالة/الملاحظة الداخلية.
router.get('/purchase-orders', purchaseOrders.list)
router.get('/purchase-orders/:id', purchaseOrders.getOne)
router.patch('/purchase-orders/:id', purchaseOrders.update)

export default router
