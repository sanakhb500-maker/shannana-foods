import { ok, fail } from '../utils/apiResponse.js'
import { mapPrismaError } from '../utils/prismaErrors.js'
import {
  inquiryListQuery, distributionListQuery, inquiryStatusBody, distributionStatusBody, idParam,
} from '../validators/forms.validators.js'
import * as svc from '../services/forms.service.js'

const errorsOf = (issues) => issues.map((i) => ({ field: i.path.join('.') || 'body', message: i.message }))

const makeList = (schema, fn) => async (req, res) => {
  const q = schema.safeParse(req.query)
  if (!q.success) return fail(res, 400, 'معاملات غير صحيحة', errorsOf(q.error.issues))
  const { rows, meta } = await fn(q.data)
  return ok(res, rows, meta)
}

const makeSetStatus = (bodySchema, fn) => async (req, res) => {
  const p = idParam.safeParse(req.params)
  if (!p.success) return fail(res, 400, 'معرّف غير صالح')
  const b = bodySchema.safeParse(req.body ?? {})
  if (!b.success) return fail(res, 400, 'بيانات غير صحيحة', errorsOf(b.error.issues))
  try {
    return ok(res, await fn(p.data.id, b.data.status))
  } catch (err) {
    const m = mapPrismaError(err)
    if (m) return fail(res, m.status, m.message)
    throw err
  }
}

export const listInquiries = makeList(inquiryListQuery, svc.listInquiries)
export const listDistribution = makeList(distributionListQuery, svc.listDistributionRequests)
export const setInquiryStatus = makeSetStatus(inquiryStatusBody, svc.setInquiryStatus)
export const setDistributionStatus = makeSetStatus(distributionStatusBody, svc.setDistributionStatus)
