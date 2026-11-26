import { prisma } from '../config/prisma.js'

const base = { id: true, name: true, nameEn: true, slug: true, description: true, descriptionEn: true, sortOrder: true, isActive: true, createdAt: true, updatedAt: true }
const withCounts = { ...base, _count: { select: { products: true } } }

const shape = async (rows) => {
  const active = await prisma.product.groupBy({ by: ['categoryId'], where: { isActive: true }, _count: { _all: true } })
  const activeBy = new Map(active.map((a) => [a.categoryId, a._count._all]))
  return rows.map(({ _count, ...c }) => ({ ...c, productsCount: _count.products, activeProductsCount: activeBy.get(c.id) ?? 0 }))
}

export async function listCategories() {
  return shape(await prisma.category.findMany({ orderBy: [{ sortOrder: 'asc' }, { id: 'asc' }], select: withCounts }))
}

export async function getCategory(id) {
  const row = await prisma.category.findUnique({ where: { id }, select: withCounts })
  return row ? (await shape([row]))[0] : null
}

async function nextSortOrder() {
  const { _max } = await prisma.category.aggregate({ _max: { sortOrder: true } })
  return (_max.sortOrder ?? 0) + 10
}

export async function createCategory(data) {
  const row = await prisma.category.create({
    data: { name: data.name, nameEn: data.nameEn ?? null, descriptionEn: data.descriptionEn ?? null, slug: data.slug, description: data.description ?? null, isActive: data.isActive ?? true, sortOrder: data.sortOrder ?? (await nextSortOrder()) },
    select: withCounts,
  })
  return (await shape([row]))[0]
}

export async function updateCategory(id, data) {
  const row = await prisma.category.update({ where: { id }, data, select: withCounts })
  return (await shape([row]))[0]
}

export const deleteCategory = (id) => prisma.category.delete({ where: { id } })
