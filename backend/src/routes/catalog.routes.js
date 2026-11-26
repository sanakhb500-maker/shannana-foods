import { Router } from 'express'
import { getCategories, getProducts } from '../controllers/catalog.controller.js'
import { publicProductTechnicalSheet } from '../controllers/technicalSheet.controller.js'

const router = Router()

// واجهة عامة للقراءة فقط (بدون مصادقة)
router.get('/categories', getCategories)
router.get('/products/:slug/technical-sheet.pdf', publicProductTechnicalSheet)
router.get('/products', getProducts)

export default router
