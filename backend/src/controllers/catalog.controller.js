import { ok, fail } from '../utils/apiResponse.js'
import { productsQuery, categoriesQuery } from '../validators/catalog.validators.js'
import { listCategories, listProducts, findActiveCategory } from '../services/catalog.service.js'

// الكتالوج يُدار من لوحة الإدارة: يجب أن تظهر تعديلات المدير فورًا. لذلك يتحقق المتصفح في كل مرة
// (ETag تلقائي من Express يعيد 304 بدون جسم عندما لا يتغير شيء، فالطلب يبقى خفيفًا).
const PUBLIC_CACHE = 'public, max-age=0, must-revalidate'

// يعيد النص الإنجليزي عند lang=en إن وُجد (غير فارغ) وإلا النص العربي. الشكل ثابت: name/description فقط.
const pick = (lang, ar, en) => (lang === 'en' && typeof en === 'string' && en.trim() ? en.trim() : ar)
const localizeCategory = (c, lang) => {
  const { nameEn, descriptionEn, ...rest } = c
  return { ...rest, name: pick(lang, c.name, nameEn), description: pick(lang, c.description, descriptionEn) }
}
const localizeProduct = (p, lang) => {
  const { nameEn, descriptionEn, category, ...rest } = p
  return {
    ...rest,
    name: pick(lang, p.name, nameEn),
    nameAr: p.name,
    nameEn: nameEn ?? null,
    description: pick(lang, p.description, descriptionEn),
    category: { id: category.id, slug: category.slug, name: pick(lang, category.name, category.nameEn) },
  }
}
const errorsOf = (issues) => issues.map((i) => ({ field: i.path.join('.'), message: i.message }))

export async function getCategories(req, res) {
  const q = categoriesQuery.safeParse(req.query)
  if (!q.success) return fail(res, 400, 'معاملات الطلب غير صحيحة', errorsOf(q.error.issues))
  const lang = q.data.lang ?? 'ar'
  const categories = (await listCategories()).map((c) => localizeCategory(c, lang))
  res.set('Cache-Control', PUBLIC_CACHE)
  return ok(res, categories, { count: categories.length, lang })
}

export async function getProducts(req, res) {
  const parsed = productsQuery.safeParse(req.query)
  if (!parsed.success) {
    return fail(res, 400, 'معاملات الطلب غير صحيحة', errorsOf(parsed.error.issues))
  }
  const { category } = parsed.data
  const lang = parsed.data.lang ?? 'ar'
  if (category && !(await findActiveCategory(category))) {
    return fail(res, 404, 'التصنيف غير موجود')
  }
  const products = (await listProducts({ category })).map((p) => localizeProduct(p, lang))
  res.set('Cache-Control', PUBLIC_CACHE)
  return ok(res, products, { count: products.length, lang, ...(category ? { category } : {}) })
}
