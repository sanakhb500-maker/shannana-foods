import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import express from 'express'
import helmet from 'helmet'
import cors from 'cors'
import cookieParser from 'cookie-parser'
import { rateLimit } from 'express-rate-limit'
import { env } from './config/env.js'
import routes from './routes/index.js'
import { apiNotFound } from './middleware/notFound.js'
import { errorHandler } from './middleware/errorHandler.js'
import { fail } from './utils/apiResponse.js'
import { createAdminPages } from './routes/adminPages.js'
import { createPublicPages } from './routes/publicPages.js'
import { requireAdmin, noStore } from './middleware/auth.js'
import formsRoutes from './routes/forms.routes.js'
import purchaseOrdersRoutes from './routes/purchaseOrders.routes.js'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const FRONTEND_DIR = path.resolve(__dirname, '../../frontend')

export function createApp() {
  const app = express()

  app.disable('x-powered-by')
  if (env.TRUST_PROXY > 0) app.set('trust proxy', env.TRUST_PROXY)

  // --- الأمان: Helmet مع CSP مضبوط ليناسب الواجهة الحالية ---
  // (الواجهة تستعمل خطوط Google، وسمات onerror/style داخل HTML، لذلك CSP الافتراضي كان سيكسرها)
  const isProd = env.NODE_ENV === 'production'
  app.use(
    helmet({
      contentSecurityPolicy: {
        directives: {
          defaultSrc: ["'self'"],
          scriptSrc: ["'self'"],
          scriptSrcAttr: ["'unsafe-inline'"], // onerror="..." الموجودة في index.html
          styleSrc: ["'self'", 'https://fonts.googleapis.com', "'unsafe-inline'"],
          fontSrc: ["'self'", 'https://fonts.gstatic.com'],
          imgSrc: ["'self'", 'data:'],
          connectSrc: ["'self'"],
          objectSrc: ["'none'"],
          frameAncestors: ["'self'"],
          upgradeInsecureRequests: isProd ? [] : null,
        },
      },
    }),
  )

  // CORS: غير لازم عندما تُخدَم الواجهة من نفس الخادم. يُفعَّل فقط إن حدّدت CORS_ORIGINS.
  if (env.CORS_ORIGINS.length > 0) {
    app.use('/api', cors({ origin: env.CORS_ORIGINS, credentials: true }))
  }

  app.use(express.json({ limit: '100kb' }))
  app.use(cookieParser()) // جلسة المدير (JWT داخل HttpOnly cookie)

  // --- تحديد معدل الطلبات على الـ API: حد عام، مع حدود أشد للنماذج العامة في forms.routes.js ---
  app.use(
    '/api',
    rateLimit({
      windowMs: 15 * 60 * 1000,
      limit: 300,
      standardHeaders: 'draft-7',
      legacyHeaders: false,
      handler: (_req, res) => fail(res, 429, 'طلبات كثيرة، يرجى المحاولة لاحقًا'),
    }),
  )

  app.use('/api', routes)
  // Public inquiry/distribution forms; mounted before the API 404 handler.
  app.use('/api', formsRoutes)
  app.use('/api', purchaseOrdersRoutes)
  app.use('/api', apiNotFound)

  // --- الواجهة الأمامية: ملفات ثابتة من مجلد frontend/ (إن وُجد) ---
  if (fs.existsSync(FRONTEND_DIR)) {
    // Protected purchase-orders page (kept separate from the existing admin page router).
    app.get('/admin/purchase-orders', noStore, requireAdmin, (_req, res, next) => {
      res.sendFile(path.join(FRONTEND_DIR, 'admin', 'purchase-orders.html'), (err) => { if (err) next(err) })
    })
    app.use('/admin', createAdminPages(FRONTEND_DIR)) // لوحة الإدارة: قبل الملفات الثابتة العامة
    app.use(createPublicPages(FRONTEND_DIR)) // الصفحة الرئيسية ثنائية اللغة (/ و/en) + robots/sitemap، قبل الملفات الثابتة
    app.use(express.static(FRONTEND_DIR))
  } else {
    console.warn(`⚠️  مجلد الواجهة غير موجود: ${FRONTEND_DIR} (الـ API يعمل، لكن الصفحات لن تُخدَم)`)
  }

  app.use(errorHandler)
  return app
}
