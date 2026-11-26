import { ok, created, fail } from '../utils/apiResponse.js'
import { readMultipart, UploadError } from '../utils/multipart.js'
import { mapPrismaError } from '../utils/prismaErrors.js'
import { createProductBody, updateProductBody, productListQuery, idParam, issuesToErrors } from '../validators/adminCatalog.validators.js'
import { inspectImage, saveProductImage, removeNewImage, existingImageUrl, MAX_IMAGE_BYTES } from '../services/imageStorage.service.js'
import * as svc from '../services/adminProducts.service.js'

const LABELS = { slug: 'هذا المعرّف (slug) مستخدم لمنتج آخر', name: 'اسم المنتج مستخدم', barcode: 'هذا الباركود مستخدم لمنتج آخر' }

// multipart (مع صورة اختيارية) أو JSON عادي
async function readInput(req) {
  if (req.is('multipart/form-data')) return readMultipart(req, { maxFileBytes: MAX_IMAGE_BYTES, fileField: 'image', maxFields: 60 })
  return { fields: req.body && typeof req.body === 'object' ? req.body : {}, file: null }
}

// عند مسح أحد طرفي الحقول المترابطة في PATCH، امسح الطرف الآخر أيضًا.
// هذا يحافظ على قيود قاعدة البيانات عند استخدام API جزئي خارج النموذج الكامل.
function completeClearedSpecificationPairs(fields) {
  const result = { ...fields }
  const has = (key) => Object.hasOwn(result, key)
  const blank = (value) => value == null || (typeof value === 'string' && value.trim() === '')
  for (const [a, b] of [['netContent', 'netContentUnit'], ['shelfLifeValue', 'shelfLifeUnit']]) {
    if (has(a) && !has(b) && blank(result[a])) result[b] = ''
    if (has(b) && !has(a) && blank(result[b])) result[a] = ''
  }
  const dimensions = ['cartonLengthCm', 'cartonWidthCm', 'cartonHeightCm']
  const suppliedDimensions = dimensions.filter(has)
  if (suppliedDimensions.length && suppliedDimensions.every((key) => blank(result[key]))) {
    for (const key of dimensions) if (!has(key)) result[key] = ''
  }
  return result
}

const sendUploadError = (res, e) => {
  if (e instanceof UploadError) {
    return fail(res, e.status, e.message, e.errors ?? (e.status === 413 ? [{ field: 'image', message: e.message }] : undefined))
  }
  throw e
}

export async function list(req, res) {
  const q = productListQuery.safeParse(req.query)
  if (!q.success) return fail(res, 400, 'معاملات البحث غير صحيحة', issuesToErrors(q.error.issues))
  const products = await svc.listProducts(q.data)
  return ok(res, products, { count: products.length })
}

export async function getOne(req, res) {
  const p = idParam.safeParse(req.params)
  if (!p.success) return fail(res, 400, 'معرّف المنتج غير صالح')
  const product = await svc.getProduct(p.data.id)
  return product ? ok(res, product) : fail(res, 404, 'المنتج غير موجود')
}

export async function create(req, res) {
  let input
  try { input = await readInput(req) } catch (e) { return sendUploadError(res, e) }

  const parsed = createProductBody.safeParse(input.fields)
  if (!parsed.success) return fail(res, 400, 'بيانات غير صحيحة', issuesToErrors(parsed.error.issues))
  const data = parsed.data
  if (!(await svc.categoryExists(data.categoryId))) return fail(res, 400, 'بيانات غير صحيحة', [{ field: 'categoryId', message: 'التصنيف غير موجود' }])

  // الصورة: ملف مرفوع (يُتحقق من محتواه) أو مسار صورة موجودة مسبقًا. لا منتج بدون صورة.
  let newUrl = null
  if (input.file) {
    const r = inspectImage(input.file.buffer)
    if (r.error) return fail(res, 400, 'الصورة غير صالحة', [{ field: 'image', message: r.error }])
    newUrl = await saveProductImage(input.file.buffer, r.info.ext)
    data.imageUrl = newUrl
  } else {
    data.imageUrl = await existingImageUrl(data.imageUrl)
    if (!data.imageUrl) return fail(res, 400, 'بيانات غير صحيحة', [{ field: 'image', message: 'الصورة مطلوبة' }])
  }

  try {
    return created(res, await svc.createProduct(data))
  } catch (err) {
    await removeNewImage(newUrl) // الملف الجديد فقط؛ لا نمسّ أي صورة قديمة
    const m = mapPrismaError(err, LABELS)
    if (m) return fail(res, m.status, m.message, m.field ? [{ field: m.field, message: m.message }] : undefined)
    throw err
  }
}

export async function update(req, res) {
  const p = idParam.safeParse(req.params)
  if (!p.success) return fail(res, 400, 'معرّف المنتج غير صالح')
  let input
  try { input = await readInput(req) } catch (e) { return sendUploadError(res, e) }

  const parsed = updateProductBody.safeParse(completeClearedSpecificationPairs(input.fields))
  if (!parsed.success) return fail(res, 400, 'بيانات غير صحيحة', issuesToErrors(parsed.error.issues))
  const data = { ...parsed.data }
  if (!input.file && Object.keys(data).length === 0) return fail(res, 400, 'لا توجد تغييرات لحفظها')
  if (!(await svc.getProduct(p.data.id))) return fail(res, 404, 'المنتج غير موجود')
  if (data.categoryId && !(await svc.categoryExists(data.categoryId))) {
    return fail(res, 400, 'بيانات غير صحيحة', [{ field: 'categoryId', message: 'التصنيف غير موجود' }])
  }

  let newUrl = null
  if (input.file) {
    const r = inspectImage(input.file.buffer)
    if (r.error) return fail(res, 400, 'الصورة غير صالحة', [{ field: 'image', message: r.error }])
    newUrl = await saveProductImage(input.file.buffer, r.info.ext) // اسم جديد: الملف القديم يبقى كما هو
    data.imageUrl = newUrl
  }

  try {
    return ok(res, await svc.updateProduct(p.data.id, data))
  } catch (err) {
    await removeNewImage(newUrl)
    const m = mapPrismaError(err, LABELS)
    if (m) return fail(res, m.status, m.message, m.field ? [{ field: m.field, message: m.message }] : undefined)
    throw err
  }
}
