import crypto from 'node:crypto'
import { prisma } from '../config/prisma.js'

const select = {
  id: true, name: true, nameEn: true, slug: true, description: true, descriptionEn: true, imageUrl: true, tone: true,
  isActive: true, isFeatured: true, sortOrder: true, categoryId: true, createdAt: true, updatedAt: true,
  // Product specifications (PHASE 11B.1)
  netContent: true, netContentUnit: true, packType: true, packTypeEn: true, packMaterial: true, packMaterialEn: true, barcode: true,
  cartonUnits: true, cartonLengthCm: true, cartonWidthCm: true, cartonHeightCm: true,
  cartonNetWeightKg: true, cartonGrossWeightKg: true,
  shelfLifeValue: true, shelfLifeUnit: true, storageNote: true, storageNoteEn: true,
  originCountry: true, hsCode: true, palletCartons: true, palletCartonsPerLayer: true, exportNote: true, exportNoteEn: true,
  category: { select: { id: true, name: true, slug: true, isActive: true } },
}

const SPEC_FIELDS = [
  'netContent', 'netContentUnit', 'packType', 'packTypeEn', 'packMaterial', 'packMaterialEn', 'barcode',
  'cartonUnits', 'cartonLengthCm', 'cartonWidthCm', 'cartonHeightCm', 'cartonNetWeightKg', 'cartonGrossWeightKg',
  'shelfLifeValue', 'shelfLifeUnit', 'storageNote', 'storageNoteEn',
  'originCountry', 'hsCode', 'palletCartons', 'palletCartonsPerLayer', 'exportNote', 'exportNoteEn',
]

function specificationData(data) {
  return Object.fromEntries(SPEC_FIELDS.map((field) => [field, data[field] ?? null]))
}

export function listProducts({ search, category, status, limit }) {
  const where = {}
  if (search) where.OR = [{ name: { contains: search, mode: 'insensitive' } }, { nameEn: { contains: search, mode: 'insensitive' } }, { slug: { contains: search, mode: 'insensitive' } }, { description: { contains: search, mode: 'insensitive' } }, { descriptionEn: { contains: search, mode: 'insensitive' } }]
  if (category) where.categoryId = category
  if (status === 'active') where.isActive = true
  if (status === 'inactive') where.isActive = false
  return prisma.product.findMany({ where, orderBy: [{ sortOrder: 'asc' }, { id: 'asc' }], take: limit ?? 200, select })
}

export const getProduct = (id) => prisma.product.findUnique({ where: { id }, select })
export const categoryExists = (id) => prisma.category.findUnique({ where: { id }, select: { id: true } })

async function nextSortOrder() {
  const { _max } = await prisma.product.aggregate({ _max: { sortOrder: true } })
  return (_max.sortOrder ?? 0) + 10
}

export async function createProduct(data) {
  const slug = data.slug ?? `product-${crypto.randomBytes(4).toString('hex')}`
  return prisma.product.create({
    data: {
      name: data.name, nameEn: data.nameEn ?? null, descriptionEn: data.descriptionEn ?? null, slug, description: data.description ?? null, categoryId: data.categoryId, imageUrl: data.imageUrl,
      tone: data.tone ?? 'gold', isActive: data.isActive ?? true, isFeatured: data.isFeatured ?? false,
      sortOrder: data.sortOrder ?? (await nextSortOrder()),
      ...specificationData(data),
    },
    select,
  })
}

export const updateProduct = (id, data) => prisma.product.update({ where: { id }, data, select })
