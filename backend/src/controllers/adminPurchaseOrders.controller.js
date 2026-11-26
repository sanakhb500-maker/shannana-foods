import { ok, fail } from '../utils/apiResponse.js'
import {
  purchaseOrderListQuery,
  purchaseOrderIdParam,
  updatePurchaseOrderBody,
} from '../validators/adminPurchaseOrders.validators.js'
import * as svc from '../services/adminPurchaseOrders.service.js'
import { PurchaseOrderNotFoundError, PurchaseOrderTransitionError } from '../services/adminPurchaseOrders.service.js'

const errorsOf = (issues) => issues.map((issue) => ({
  field: issue.path.join('.') || 'body',
  message: issue.message,
}))

export async function list(req, res) {
  const parsed = purchaseOrderListQuery.safeParse(req.query)
  if (!parsed.success) return fail(res, 400, 'معاملات البحث غير صحيحة', errorsOf(parsed.error.issues))
  const result = await svc.listPurchaseOrders(parsed.data)
  return ok(res, result.rows, result.meta)
}

export async function getOne(req, res) {
  const parsed = purchaseOrderIdParam.safeParse(req.params)
  if (!parsed.success) return fail(res, 400, 'رقم الطلب غير صالح', errorsOf(parsed.error.issues))
  const order = await svc.getPurchaseOrder(parsed.data.id)
  return order ? ok(res, order) : fail(res, 404, 'الطلب غير موجود')
}

export async function update(req, res) {
  const params = purchaseOrderIdParam.safeParse(req.params)
  if (!params.success) return fail(res, 400, 'رقم الطلب غير صالح', errorsOf(params.error.issues))

  const body = updatePurchaseOrderBody.safeParse(req.body ?? {})
  if (!body.success) return fail(res, 400, 'بيانات التحديث غير صحيحة', errorsOf(body.error.issues))

  try {
    const order = await svc.updatePurchaseOrder(params.data.id, body.data)
    return ok(res, order)
  } catch (error) {
    if (error instanceof PurchaseOrderNotFoundError) return fail(res, 404, 'الطلب غير موجود')
    if (error instanceof PurchaseOrderTransitionError) {
      return fail(res, 409, 'لا يمكن نقل الطلب من حالته الحالية إلى الحالة المطلوبة. اتبعي تسلسل حالات الطلب.', [
        { field: 'status', message: `الانتقال غير مسموح: ${error.from} → ${error.to}` },
      ])
    }
    throw error
  }
}
