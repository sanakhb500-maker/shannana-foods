import { prisma } from '../config/prisma.js'

const DEDUP_INQUIRY_MS = 10 * 60 * 1000
const DEDUP_DISTRIBUTION_MS = 60 * 60 * 1000

// ---------- عام ----------
export const findActiveProduct = (id) => prisma.product.findFirst({ where: { id, isActive: true, category: { isActive: true } }, select: { id: true } })

// يعيد { duplicate: true } إن وُجد الطلب نفسه حديثًا (إرسال مكرر / ضغط مزدوج)
export async function createInquiry(d) {
  const contact = [...(d.phone ? [{ phone: d.phone }] : []), ...(d.email ? [{ email: d.email }] : [])]
  const dup = await prisma.inquiry.findFirst({
    where: { createdAt: { gte: new Date(Date.now() - DEDUP_INQUIRY_MS) }, message: d.message, OR: contact },
    select: { id: true },
  })
  if (dup) return { duplicate: true }
  await prisma.inquiry.create({
    data: { name: d.name, phone: d.phone ?? null, email: d.email ?? null, message: d.message, productId: d.productId ?? null, source: d.productId ? 'product' : 'contact' },
  })
  return { duplicate: false }
}

export async function createDistributionRequest(d) {
  const dup = await prisma.distributionRequest.findFirst({
    where: { createdAt: { gte: new Date(Date.now() - DEDUP_DISTRIBUTION_MS) }, phone: d.phone, businessName: d.businessName },
    select: { id: true },
  })
  if (dup) return { duplicate: true }
  await prisma.distributionRequest.create({
    data: {
      name: d.name, businessName: d.businessName, phone: d.phone, whatsapp: d.whatsapp ?? null, email: d.email ?? null,
      city: d.city ?? null, area: d.area ?? null, businessType: d.businessType, message: d.message ?? null,
    },
  })
  return { duplicate: false }
}

// ---------- لوحة المدير ----------
const contains = (v) => ({ contains: v, mode: 'insensitive' })

async function paginate(model, { where, page, pageSize, include }) {
  const [total, rows] = await Promise.all([
    model.count({ where }),
    model.findMany({ where, orderBy: [{ createdAt: 'desc' }, { id: 'desc' }], skip: (page - 1) * pageSize, take: pageSize, ...(include ? { include } : {}) }),
  ])
  return { rows, meta: { page, pageSize, total, totalPages: Math.max(1, Math.ceil(total / pageSize)) } }
}

export function listInquiries({ status, search, page, pageSize }) {
  const where = {}
  if (status !== 'all') where.status = status
  if (search) where.OR = [{ name: contains(search) }, { phone: contains(search) }, { email: contains(search) }, { message: contains(search) }]
  return paginate(prisma.inquiry, { where, page, pageSize, include: { product: { select: { id: true, name: true } } } })
}

export function listDistributionRequests({ status, search, page, pageSize }) {
  const where = {}
  if (status !== 'all') where.status = status
  if (search) where.OR = ['name', 'businessName', 'phone', 'whatsapp', 'email', 'city', 'area', 'message'].map((f) => ({ [f]: contains(search) }))
  return paginate(prisma.distributionRequest, { where, page, pageSize })
}

export const setInquiryStatus = (id, status) => prisma.inquiry.update({ where: { id }, data: { status }, include: { product: { select: { id: true, name: true } } } })
export const setDistributionStatus = (id, status) => prisma.distributionRequest.update({ where: { id }, data: { status } })
