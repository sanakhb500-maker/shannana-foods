import { prisma } from '../config/prisma.js'

const MAX_INT = 2_147_483_647

export class PurchaseOrderValidationError extends Error {
  constructor(code, field = 'items') {
    super(code)
    this.name = 'PurchaseOrderValidationError'
    this.code = code
    this.field = field
  }
}

/**
 * Save a guest purchase order and its item snapshots atomically.
 * Prices remain NULL in this phase, and inventory is never changed here.
 */
export async function createPurchaseOrder(data) {
  return prisma.$transaction(async (tx) => {
    const productIds = data.items.map((item) => item.productId)
    const products = await tx.product.findMany({
      where: {
        id: { in: productIds },
        isActive: true,
        category: { isActive: true },
      },
      select: {
        id: true,
        name: true,
        nameEn: true,
        slug: true,
        cartonUnits: true,
      },
    })

    const productsById = new Map(products.map((product) => [product.id, product]))
    if (productsById.size !== productIds.length) {
      throw new PurchaseOrderValidationError('PRODUCT_UNAVAILABLE')
    }

    const itemSnapshots = data.items.map((item) => {
      const product = productsById.get(item.productId)
      if (item.cartonQuantity > 0 && (!Number.isInteger(product.cartonUnits) || product.cartonUnits <= 0)) {
        throw new PurchaseOrderValidationError('CARTON_NOT_AVAILABLE')
      }

      const unitsPerCarton = product.cartonUnits ?? null
      const totalUnitsBig = (BigInt(item.cartonQuantity) * BigInt(unitsPerCarton ?? 0)) + BigInt(item.unitQuantity)
      if (totalUnitsBig < 1n || totalUnitsBig > BigInt(MAX_INT)) {
        throw new PurchaseOrderValidationError('INVALID_TOTAL_QUANTITY')
      }

      return {
        productId: product.id,
        productName: product.name,
        productNameEn: product.nameEn,
        productSlug: product.slug,
        cartonQuantity: item.cartonQuantity,
        unitQuantity: item.unitQuantity,
        unitsPerCarton,
        totalUnits: Number(totalUnitsBig),
        // No quote/pricing is recorded at order-submission time.
        unitPrice: null,
        priceCurrency: null,
      }
    })

    return tx.purchaseOrder.create({
      data: {
        customerName: data.customerName,
        phone: data.phone,
        businessName: data.businessName,
        city: data.city,
        area: data.area ?? null,
        address: data.address ?? null,
        email: data.email ?? null,
        whatsapp: data.whatsapp ?? null,
        customerNote: data.customerNote ?? null,
        status: 'new',
        items: { create: itemSnapshots },
      },
      select: {
        id: true,
        status: true,
        createdAt: true,
        items: {
          select: {
            productName: true,
            productNameEn: true,
            cartonQuantity: true,
            unitQuantity: true,
            unitsPerCarton: true,
            totalUnits: true,
          },
          orderBy: { id: 'asc' },
        },
      },
    })
  })
}
