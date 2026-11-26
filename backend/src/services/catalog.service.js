import { prisma } from '../config/prisma.js'

// التصنيفات النشطة مرتبة، مع عدد منتجاتها النشطة (تستعمله الواجهة لإخفاء الفلاتر الفارغة).
export async function listCategories() {
  const rows = await prisma.category.findMany({
    where: { isActive: true },
    orderBy: [{ sortOrder: 'asc' }, { id: 'asc' }],
    select: {
      id: true,
      name: true,
      nameEn: true,
      slug: true,
      description: true,
      descriptionEn: true,
      sortOrder: true,
      _count: { select: { products: { where: { isActive: true } } } },
    },
  })
  return rows.map(({ _count, ...category }) => ({ ...category, productsCount: _count.products }))
}

export function findActiveCategory(slug) {
  return prisma.category.findFirst({ where: { slug, isActive: true }, select: { id: true } })
}

// المنتجات النشطة فقط، وتصنيفها نشط أيضًا. تصفية اختيارية بـ slug التصنيف.
export function listProducts({ category } = {}) {
  return prisma.product.findMany({
    where: { isActive: true, category: { isActive: true, ...(category ? { slug: category } : {}) } },
    orderBy: [{ sortOrder: 'asc' }, { id: 'asc' }],
    select: {
      id: true,
      name: true,
      nameEn: true,
      slug: true,
      description: true,
      descriptionEn: true,
      imageUrl: true,
      tone: true,
      cartonUnits: true,
      isFeatured: true,
      sortOrder: true,
      category: { select: { id: true, name: true, nameEn: true, slug: true } },
    },
  })
}
