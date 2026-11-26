import { prisma } from '../config/prisma.js'

// Public product details: only active products under active categories are visible.
// Select only consumer-facing specifications. Commercial/export fields stay private.
export function findPublicProductBySlug(slug) {
  return prisma.product.findFirst({
    where: { slug, isActive: true, category: { isActive: true } },
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
      updatedAt: true,
      // Consumer-facing specifications only
      netContent: true,
      netContentUnit: true,
      packType: true,
      packTypeEn: true,
      packMaterial: true,
      packMaterialEn: true,
      shelfLifeValue: true,
      shelfLifeUnit: true,
      storageNote: true,
      storageNoteEn: true,
      originCountry: true,
      category: { select: { name: true, nameEn: true, slug: true } },
    },
  })
}

// Sitemap includes only products publicly visible on the website.
export function listSitemapProducts() {
  return prisma.product.findMany({
    where: { isActive: true, category: { isActive: true } },
    orderBy: [{ sortOrder: 'asc' }, { id: 'asc' }],
    select: { slug: true, updatedAt: true },
  })
}

// Use the English value when present; otherwise gracefully fall back to Arabic.
export const pick = (lang, ar, en) =>
  lang === 'en' && typeof en === 'string' && en.trim() ? en.trim() : ar
