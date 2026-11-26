import { prisma } from '../config/prisma.js'

// إحصاءات لوحة التحكم (أعداد فقط).
export async function getStats() {
  const [productsTotal, productsActive, categoriesTotal, categoriesActive, inquiriesTotal, inquiriesNew, distTotal, distNew] =
    await Promise.all([
      prisma.product.count(),
      prisma.product.count({ where: { isActive: true } }),
      prisma.category.count(),
      prisma.category.count({ where: { isActive: true } }),
      prisma.inquiry.count(),
      prisma.inquiry.count({ where: { status: 'new' } }),
      prisma.distributionRequest.count(),
      prisma.distributionRequest.count({ where: { status: 'new' } }),
    ])
  return {
    products: { total: productsTotal, active: productsActive },
    categories: { total: categoriesTotal, active: categoriesActive },
    inquiries: { total: inquiriesTotal, new: inquiriesNew },
    distributionRequests: { total: distTotal, new: distNew },
  }
}
