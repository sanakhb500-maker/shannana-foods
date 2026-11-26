// غلاف الاستجابة الموحّد:
//   نجاح: { success: true, data, meta? }      فشل: { success: false, message, errors? }
export const ok = (res, data, meta, status = 200) =>
  res.status(status).json(meta ? { success: true, data, meta } : { success: true, data })

export const created = (res, data) => ok(res, data, undefined, 201)

export const fail = (res, status, message, errors) =>
  res.status(status).json(errors ? { success: false, message, errors } : { success: false, message })
