import busboy from 'busboy'

export class UploadError extends Error {
  constructor(status, message, errors) {
    super(message)
    this.status = status
    this.errors = errors
  }
}

// يقرأ multipart/form-data في الذاكرة بحدود صارمة. لا يكتب شيئًا على القرص.
// حقل الملف الوحيد المسموح: fileField. اسم الملف الأصلي لا يُستعمل أبدًا.
export function readMultipart(req, { maxFileBytes, fileField = 'image', maxFields = 20, maxFieldBytes = 16 * 1024 }) {
  return new Promise((resolve, reject) => {
    const maxBody = maxFileBytes + maxFields * maxFieldBytes + 64 * 1024
    const declared = Number(req.headers['content-length'])
    if (Number.isFinite(declared) && declared > maxBody) {
      return reject(new UploadError(413, 'حجم الطلب أكبر من الحد المسموح'))
    }

    let bb
    try {
      bb = busboy({
        headers: req.headers,
        defParamCharset: 'utf8',
        limits: { fileSize: maxFileBytes, files: 1, fields: maxFields, fieldSize: maxFieldBytes, parts: maxFields + 2 },
      })
    } catch {
      return reject(new UploadError(400, 'طلب الرفع غير صالح'))
    }

    const fields = {}
    let file = null
    let failure = null
    let received = 0
    let settled = false
    const rejectOnce = (error) => {
      if (settled) return
      settled = true
      reject(error)
    }
    const setFailure = (status, message) => { failure ??= new UploadError(status, message) }

    bb.on('field', (name, value, info) => {
      if (info.nameTruncated || info.valueTruncated) return setFailure(413, 'قيمة أحد الحقول طويلة جدًا')
      if (Object.hasOwn(fields, name)) return setFailure(400, 'حقل مكرر في الطلب')
      fields[name] = value
    })
    bb.on('file', (name, stream, info) => {
      if (name !== fileField) { setFailure(400, 'حقل ملف غير متوقع'); return stream.resume() }
      if (!info.filename) return stream.resume() // حقل ملف فارغ (لم يُختر ملف)
      const chunks = []
      let size = 0
      stream.on('data', (c) => { chunks.push(c); size += c.length })
      stream.on('limit', () => setFailure(413, `حجم الصورة أكبر من الحد المسموح (${Math.round(maxFileBytes / 1048576)} ميغابايت)`))
      stream.on('end', () => { if (!stream.truncated && size > 0) file = { buffer: Buffer.concat(chunks, size), size } })
    })
    bb.on('filesLimit', () => setFailure(400, 'يُسمح بصورة واحدة فقط'))
    bb.on('fieldsLimit', () => setFailure(400, 'عدد الحقول أكبر من المسموح'))
    bb.on('partsLimit', () => setFailure(400, 'عدد أجزاء الطلب أكبر من المسموح'))
    bb.on('error', () => rejectOnce(new UploadError(400, 'تعذّر قراءة طلب الرفع')))
    bb.on('close', () => {
      if (failure) return rejectOnce(failure)
      if (settled) return
      settled = true
      resolve({ fields, file })
    })

    // طلب بدون Content-Length (chunked) يتجاوز الحد: نوقف القراءة فورًا.
    req.on('data', (chunk) => {
      received += chunk.length
      if (received > maxBody) {
        req.unpipe(bb)
        rejectOnce(new UploadError(413, 'حجم الطلب أكبر من الحد المسموح'))
      }
      if (received > maxBody * 4) req.destroy()
    })
    req.on('error', () => rejectOnce(new UploadError(400, 'انقطع الطلب')))
    req.pipe(bb)
  })
}
