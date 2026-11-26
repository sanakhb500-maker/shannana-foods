import { ok, created, fail } from '../utils/apiResponse.js'
import { mapPrismaError } from '../utils/prismaErrors.js'
import { createCategoryBody, updateCategoryBody, idParam, issuesToErrors } from '../validators/adminCatalog.validators.js'
import * as svc from '../services/adminCategories.service.js'

const LABELS = { slug: 'هذا المعرّف (slug) مستخدم لتصنيف آخر', name: 'اسم التصنيف مستخدم لتصنيف آخر' }
const prismaFail = (res, err) => {
  const m = mapPrismaError(err, LABELS)
  if (!m) throw err
  return fail(res, m.status, m.message, m.field ? [{ field: m.field, message: m.message }] : undefined)
}

export async function list(_req, res) {
  const categories = await svc.listCategories()
  return ok(res, categories, { count: categories.length })
}

export async function create(req, res) {
  const parsed = createCategoryBody.safeParse(req.body ?? {})
  if (!parsed.success) return fail(res, 400, 'بيانات غير صحيحة', issuesToErrors(parsed.error.issues))
  try { return created(res, await svc.createCategory(parsed.data)) } catch (err) { return prismaFail(res, err) }
}

export async function update(req, res) {
  const p = idParam.safeParse(req.params)
  if (!p.success) return fail(res, 400, 'معرّف التصنيف غير صالح')
  const parsed = updateCategoryBody.safeParse(req.body ?? {})
  if (!parsed.success) return fail(res, 400, 'بيانات غير صحيحة', issuesToErrors(parsed.error.issues))
  if (Object.keys(parsed.data).length === 0) return fail(res, 400, 'لا توجد تغييرات لحفظها')
  try { return ok(res, await svc.updateCategory(p.data.id, parsed.data)) } catch (err) { return prismaFail(res, err) }
}

// الحذف مسموح فقط لتصنيف فارغ. التصنيف الذي فيه منتجات يُعطَّل بدل حذفه.
export async function remove(req, res) {
  const p = idParam.safeParse(req.params)
  if (!p.success) return fail(res, 400, 'معرّف التصنيف غير صالح')
  const cat = await svc.getCategory(p.data.id)
  if (!cat) return fail(res, 404, 'التصنيف غير موجود')
  if (cat.productsCount > 0) {
    return fail(res, 409, `لا يمكن حذف تصنيف يحتوي ${cat.productsCount} منتجًا. انقل المنتجات أو عطّل التصنيف بدل حذفه.`)
  }
  try { await svc.deleteCategory(p.data.id) } catch (err) { return prismaFail(res, err) }
  return ok(res, { deleted: true })
}
