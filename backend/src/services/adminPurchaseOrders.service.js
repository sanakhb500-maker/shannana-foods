import { prisma } from '../config/prisma.js'

const contains = (value) => ({ contains: value, mode: 'insensitive' })

// كل طلب ينتقل خطوة واحدة للأمام. الرفض ممكن أثناء المراجعة أو انتظار موافقة العميل.
// الحالات النهائية لا يمكن إعادة فتحها من هذه الواجهة.
const ALLOWED_NEXT = Object.freeze({
  new: ['under_review', 'rejected'],
  under_review: ['awaiting_customer_approval', 'rejected'],
  awaiting_customer_approval: ['confirmed', 'rejected'],
  confirmed: [],
  rejected: [],
})

export class PurchaseOrderNotFoundError extends Error {
  constructor() {
    super('PURCHASE_ORDER_NOT_FOUND')
    this.name = 'PurchaseOrderNotFoundError'
  }
}

export class PurchaseOrderTransitionError extends Error {
  constructor(from, to) {
    super('PURCHASE_ORDER_INVALID_TRANSITION')
    this.name = 'PurchaseOrderTransitionError'
    this.from = from
    this.to = to
  }
}

function buildWhere({ status, search }) {
  const where = {}
  if (status !== 'all') where.status = status

  if (search) {
    const or = [
      { customerName: contains(search) },
      { businessName: contains(search) },
      { phone: contains(search) },
      { whatsapp: contains(search) },
      { email: contains(search) },
      { city: contains(search) },
      { area: contains(search) },
    ]

    const referenceMatch = search.match(/^SF-(\d+)$/i)
    const numericMatch = search.match(/^\d+$/)
    const idText = referenceMatch?.[1] ?? numericMatch?.[0]
    if (idText) {
      const id = Number(idText)
      if (Number.isSafeInteger(id) && id > 0 && id <= 2_147_483_647) or.push({ id })
    }
    where.OR = or
  }

  return where
}

export async function listPurchaseOrders({ status, search, page, pageSize }) {
  const where = buildWhere({ status, search })
  const [total, rows, groupedCounts] = await Promise.all([
    prisma.purchaseOrder.count({ where }),
    prisma.purchaseOrder.findMany({
      where,
      orderBy: [{ createdAt: 'desc' }, { id: 'desc' }],
      skip: (page - 1) * pageSize,
      take: pageSize,
      include: {
        items: {
          select: {
            id: true,
            productName: true,
            cartonQuantity: true,
            unitQuantity: true,
            unitsPerCarton: true,
            totalUnits: true,
          },
          orderBy: { id: 'asc' },
        },
      },
    }),
    prisma.purchaseOrder.groupBy({ by: ['status'], _count: { _all: true } }),
  ])

  const counts = {
    new: 0,
    under_review: 0,
    awaiting_customer_approval: 0,
    confirmed: 0,
    rejected: 0,
  }
  for (const group of groupedCounts) counts[group.status] = group._count._all
  counts.all = Object.values(counts).reduce((sum, count) => sum + count, 0)

  return {
    rows,
    meta: {
      page,
      pageSize,
      total,
      totalPages: Math.max(1, Math.ceil(total / pageSize)),
      counts,
    },
  }
}

export function getPurchaseOrder(id) {
  return prisma.purchaseOrder.findUnique({
    where: { id },
    include: { items: { orderBy: { id: 'asc' } } },
  })
}

export async function updatePurchaseOrder(id, changes) {
  return prisma.$transaction(async (tx) => {
    const current = await tx.purchaseOrder.findUnique({
      where: { id },
      select: { id: true, status: true },
    })
    if (!current) throw new PurchaseOrderNotFoundError()

    if (changes.status && changes.status !== current.status) {
      const allowed = ALLOWED_NEXT[current.status] ?? []
      if (!allowed.includes(changes.status)) {
        throw new PurchaseOrderTransitionError(current.status, changes.status)
      }
    }

    const data = {}
    if (changes.status !== undefined) data.status = changes.status
    if (Object.hasOwn(changes, 'adminNote')) {
      // Empty text clears the private note; notes are never exposed by public endpoints.
      data.adminNote = changes.adminNote.trim() ? changes.adminNote : null
    }

    return tx.purchaseOrder.update({
      where: { id },
      data,
      include: { items: { orderBy: { id: 'asc' } } },
    })
  })
}
