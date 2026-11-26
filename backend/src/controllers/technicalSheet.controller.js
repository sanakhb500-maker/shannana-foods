import { fail } from '../utils/apiResponse.js'
import { idParam } from '../validators/adminCatalog.validators.js'
import { getProduct } from '../services/adminProducts.service.js'
import { findPublicProductBySlug } from '../services/productPage.service.js'
import { generateTechnicalSheetPdfBuffer, technicalSheetFilename } from '../services/technicalSheetPdf.service.js'

const PUBLIC_SLUG = /^[a-z0-9]+(?:-[a-z0-9]+)*$/

function sendPdf(res, buffer, filename) {
  return res.status(200).set({
    'Content-Type': 'application/pdf',
    'Content-Disposition': `attachment; filename="${filename}"`,
    'Content-Length': String(buffer.length),
    'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff',
  }).send(buffer)
}

// Mounted only inside /api/admin, after noStore + originGuard + requireAdmin.
export async function adminProductTechnicalSheet(req, res) {
  const parsed = idParam.safeParse(req.params)
  if (!parsed.success) return fail(res, 400, 'معرّف المنتج غير صالح')

  const product = await getProduct(parsed.data.id)
  if (!product) return fail(res, 404, 'المنتج غير موجود')

  const buffer = await generateTechnicalSheetPdfBuffer(product, { audience: 'admin' })
  return sendPdf(res, buffer, technicalSheetFilename(product))
}

// Public endpoint intentionally uses the same active-product + active-category query as the product page.
export async function publicProductTechnicalSheet(req, res) {
  const { slug } = req.params
  if (typeof slug !== 'string' || slug.length > 150 || !PUBLIC_SLUG.test(slug)) {
    return fail(res, 404, 'المنتج غير موجود')
  }

  const product = await findPublicProductBySlug(slug)
  if (!product) return fail(res, 404, 'المنتج غير موجود')

  const buffer = await generateTechnicalSheetPdfBuffer(product, { audience: 'public' })
  return sendPdf(res, buffer, technicalSheetFilename(product))
}
