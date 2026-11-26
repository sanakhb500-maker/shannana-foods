import fs from 'node:fs/promises'
import path from 'node:path'
import crypto from 'node:crypto'
import { sniffImage } from '../utils/imageInfo.js'
import { IMG_DIR, PRODUCT_IMG_DIR, PRODUCT_IMG_URL_PREFIX } from '../config/paths.js'

export const MAX_IMAGE_BYTES = 8 * 1024 * 1024 // 8 ميغابايت
export const MIN_SIDE = 300
export const MAX_SIDE = 8000

// يتحقق من المحتوى الفعلي (وليس الامتداد ولا Content-Type المُرسَل). يعيد {ext,type,width,height} أو يرمي رسالة.
export function inspectImage(buffer) {
  const info = sniffImage(buffer)
  if (!info) return { error: 'الملف ليس صورة صالحة. المسموح: JPEG أو PNG أو WebP.' }
  if (!info.width || !info.height) return { error: 'تعذّر قراءة أبعاد الصورة.' }
  if (info.width < MIN_SIDE || info.height < MIN_SIDE) return { error: `الصورة صغيرة جدًا (الحد الأدنى ${MIN_SIDE}×${MIN_SIDE} بكسل).` }
  if (info.width > MAX_SIDE || info.height > MAX_SIDE) return { error: `الصورة كبيرة جدًا (الحد الأقصى ${MAX_SIDE}×${MAX_SIDE} بكسل).` }
  return { info }
}

// يحفظ بملف جديد باسم مولَّد (لا يستبدل ولا يحذف أي ملف موجود) ويعيد المسار العام.
export async function saveProductImage(buffer, ext) {
  await fs.mkdir(PRODUCT_IMG_DIR, { recursive: true })
  const name = `p-${crypto.randomUUID()}.${ext}`
  await fs.writeFile(path.join(PRODUCT_IMG_DIR, name), buffer, { flag: 'wx', mode: 0o644 }) // wx: يفشل إن وُجد الاسم
  return PRODUCT_IMG_URL_PREFIX + name
}

// حذف ملف جديد أُنشئ في الطلب الحالي فقط (عند فشل حفظ المنتج). لا يُستعمل على الصور القديمة.
export async function removeNewImage(url) {
  if (typeof url !== 'string' || !url.startsWith(PRODUCT_IMG_URL_PREFIX)) return
  const name = url.slice(PRODUCT_IMG_URL_PREFIX.length)
  if (!/^p-[0-9a-f-]{36}\.(png|jpg|webp)$/.test(name)) return
  await fs.rm(path.join(PRODUCT_IMG_DIR, name), { force: true }).catch(() => {})
}

// استعمال صورة موجودة أصلًا في frontend/img (مثلاً الصور الحالية): مسار صارم + وجود الملف.
export async function existingImageUrl(url) {
  if (typeof url !== 'string') return null
  const m = /^\/img\/((?:products\/)?[A-Za-z0-9][A-Za-z0-9._-]{0,120}\.(?:png|jpe?g|webp))$/i.exec(url)
  if (!m || m[1].includes('..')) return null
  const full = path.join(IMG_DIR, m[1])
  if (!full.startsWith(IMG_DIR + path.sep)) return null
  try { return (await fs.stat(full)).isFile() ? url : null } catch { return null }
}
