import { Router } from 'express'
import { prisma } from '../config/prisma.js'
import { ok, fail } from '../utils/apiResponse.js'
import catalogRoutes from './catalog.routes.js'
import authRoutes from './auth.routes.js'
import adminRoutes from './admin.routes.js'

const router = Router()

// GET /api/health — يتأكد أن الخادم يعمل وأن قاعدة البيانات تستجيب.
router.get('/health', async (_req, res) => {
  try {
    await prisma.$queryRaw`SELECT 1`
    return ok(res, { status: 'ok', database: 'connected' })
  } catch (err) {
    console.error('Health check: database unreachable —', err.message)
    return fail(res, 503, 'قاعدة البيانات غير متاحة')
  }
})

// المسارات العامة للمنتجات والتصنيفات (PHASE 6)
router.use(catalogRoutes)

// المصادقة ولوحة الإدارة (PHASE 7)
router.use('/auth', authRoutes)
router.use('/admin', adminRoutes)

// مسارات المراحل القادمة (الإدارة، النماذج...) تُضاف هنا.

export default router
