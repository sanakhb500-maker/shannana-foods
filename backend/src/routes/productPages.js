import fs from 'node:fs'
import path from 'node:path'
import { createRenderer } from '../i18n/render.js'
import { tr } from '../i18n/product.js'
import { env } from '../config/env.js'
import { findPublicProductBySlug, pick } from '../services/productPage.service.js'

const SLUG = /^[a-z0-9]+(?:-[a-z0-9]+)*$/
const TONES = new Set(['red', 'gold', 'rose', 'green'])
const OG_LOCALE = { ar: 'ar_AR', en: 'en_US' }

const esc = (v) => String(v ?? '').replace(/[&<>"']/g, (c) => ({
  '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
})[c])

export const productPath = (lang, slug) => `${lang === 'en' ? '/en' : ''}/products/${encodeURIComponent(slug)}`

function metaDescription(text) {
  const one = String(text ?? '').replace(/\s+/g, ' ').trim()
  if (one.length <= 160) return one
  const cut = one.slice(0, 157)
  return cut.slice(0, cut.lastIndexOf(' ') > 80 ? cut.lastIndexOf(' ') : 157) + '…'
}

const safeImage = (u) => (typeof u === 'string' && /^\/(?!\/)/.test(u) ? u : '')

function seoHead({ lang, product, base, title, description, imageAbs }) {
  const alt = lang === 'ar' ? 'en' : 'ar'
  if (!product) {
    return `<title>${esc(title)}</title>\n  <meta name="robots" content="noindex, nofollow" />`
  }
  const urls = { ar: base + productPath('ar', product.slug), en: base + productPath('en', product.slug) }
  const ld = {
    '@context': 'https://schema.org',
    '@type': 'Product',
    name: pick(lang, product.name, product.nameEn),
    description,
    ...(imageAbs ? { image: [imageAbs] } : {}),
    category: pick(lang, product.category.name, product.category.nameEn),
    brand: { '@type': 'Brand', name: 'Shannana Foods' },
    url: urls[lang],
    inLanguage: lang,
  }
  return [
    `<title>${esc(title)}</title>`,
    `<meta name="description" content="${esc(description)}" />`,
    `<link rel="canonical" href="${esc(urls[lang])}" />`,
    `<link rel="alternate" hreflang="ar" href="${esc(urls.ar)}" />`,
    `<link rel="alternate" hreflang="en" href="${esc(urls.en)}" />`,
    `<link rel="alternate" hreflang="x-default" href="${esc(urls.ar)}" />`,
    `<meta property="og:type" content="website" />`,
    `<meta property="og:site_name" content="Shannana Foods" />`,
    `<meta property="og:title" content="${esc(title)}" />`,
    `<meta property="og:description" content="${esc(description)}" />`,
    `<meta property="og:url" content="${esc(urls[lang])}" />`,
    `<meta property="og:locale" content="${OG_LOCALE[lang]}" />`,
    `<meta property="og:locale:alternate" content="${OG_LOCALE[alt]}" />`,
    ...(imageAbs ? [`<meta property="og:image" content="${esc(imageAbs)}" />`] : []),
    `<script type="application/ld+json">${JSON.stringify(ld).replace(/</g, '\\u003c')}</script>`,
  ].join('\n  ')
}

function formatDecimal(value) {
  if (value === null || value === undefined || value === '') return ''
  // Keep Decimal precision; format the decimal string without converting to Number.
  return String(value).replace(/(\.\d*?[1-9])0+$/, '$1').replace(/\.0+$/, '')
}

function countryName(code, lang) {
  const normalized = String(code ?? '').trim().toUpperCase()
  if (!/^[A-Z]{2}$/.test(normalized)) return ''
  if (normalized === 'SY') return lang === 'en' ? 'Syria' : 'سوريا'
  try {
    const display = new Intl.DisplayNames([lang === 'en' ? 'en' : 'ar'], { type: 'region' }).of(normalized)
    return display && display !== normalized ? display : normalized
  } catch {
    return normalized
  }
}

function pickAvailable(lang, ar, en) {
  const preferred = lang === 'en' ? en : ar
  const fallback = lang === 'en' ? ar : en
  if (typeof preferred === 'string' && preferred.trim()) return preferred.trim()
  if (typeof fallback === 'string' && fallback.trim()) return fallback.trim()
  return ''
}

function buildConsumerSpecsMarkup(product, lang) {
  const cards = []
  const rows = []

  if (product.netContent !== null && product.netContent !== undefined && product.netContentUnit) {
    const unit = tr(lang, `spec.unit.${product.netContentUnit}`)
    const amount = formatDecimal(product.netContent)
    if (amount && unit !== `spec.unit.${product.netContentUnit}`) {
      cards.push({ label: tr(lang, 'spec.netContent'), value: `${amount} ${unit}`, icon: '◉' })
    }
  }

  const packType = pickAvailable(lang, product.packType, product.packTypeEn)
  if (typeof packType === 'string' && packType.trim()) {
    cards.push({ label: tr(lang, 'spec.packType'), value: packType.trim(), icon: '▣' })
  }

  const packMaterial = pickAvailable(lang, product.packMaterial, product.packMaterialEn)
  if (typeof packMaterial === 'string' && packMaterial.trim()) {
    cards.push({ label: tr(lang, 'spec.packMaterial'), value: packMaterial.trim(), icon: '◇' })
  }

  if (product.shelfLifeValue !== null && product.shelfLifeValue !== undefined && product.shelfLifeUnit) {
    const unit = tr(lang, `spec.time.${product.shelfLifeUnit}`)
    if (unit !== `spec.time.${product.shelfLifeUnit}`) {
      const amount = String(product.shelfLifeValue)
      const displayUnit = lang === 'en' && amount === '1' ? unit : (lang === 'en' ? `${unit}s` : unit)
      rows.push({ label: tr(lang, 'spec.shelfLife'), value: `${amount} ${displayUnit}` })
    }
  }

  const storageNote = pickAvailable(lang, product.storageNote, product.storageNoteEn)
  if (typeof storageNote === 'string' && storageNote.trim()) {
    rows.push({ label: tr(lang, 'spec.storage'), value: storageNote.trim() })
  }

  const origin = countryName(product.originCountry, lang)
  if (origin) rows.push({ label: tr(lang, 'spec.originCountry'), value: origin })

  // Do not render an empty specifications section on products without consumer data.
  if (!cards.length && !rows.length) return ''

  const cardsHtml = cards.length
    ? `<div class="pd-spec-cards">${cards.map((item) => `
        <article class="pd-spec-card">
          <span class="pd-spec-icon" aria-hidden="true">${esc(item.icon)}</span>
          <div class="pd-spec-card-copy"><span>${esc(item.label)}</span><strong>${esc(item.value)}</strong></div>
        </article>`).join('')}
      </div>`
    : ''

  const rowsHtml = rows.length
    ? `<dl class="pd-spec-list">${rows.map((item) => `
        <div class="pd-spec-row"><dt>${esc(item.label)}</dt><dd>${esc(item.value)}</dd></div>`).join('')}
      </dl>`
    : ''

  return `
    <section class="pd-specs" aria-labelledby="pdSpecsTitle">
      <div class="pd-specs-heading">
        <span class="pd-specs-kicker">${esc(tr(lang, 'spec.kicker'))}</span>
        <h2 id="pdSpecsTitle">${esc(tr(lang, 'spec.title'))}</h2>
      </div>
      ${cardsHtml}
      ${rowsHtml}
    </section>`
}

// Fill the server-rendered product template, including optional consumer specs.
function fill(html, { lang, slug, product, base }) {
  const found = Boolean(product)
  const name = found ? pick(lang, product.name, product.nameEn) : ''
  const category = found ? pick(lang, product.category.name, product.category.nameEn) : ''
  const description = found ? pick(lang, product.description, product.descriptionEn) ?? '' : ''
  const image = found ? safeImage(product.imageUrl) : ''
  const title = found ? tr(lang, 'seo.title', { name }) : tr(lang, 'nf.seoTitle')
  const metaDesc = found ? metaDescription(description) || tr(lang, 'seo.fallbackDescription', { name, category }) : ''
  const wa = `https://wa.me/${env.WHATSAPP_NUMBER}?text=${encodeURIComponent(tr(lang, 'wa.text', { name }))}`

  const values = {
    home: lang === 'en' ? '/en' : '/',
    'langhref.ar': productPath('ar', slug),
    'langhref.en': productPath('en', slug),
    productId: found ? product.id : '',
    technicalSheetUrl: found ? `/api/products/${encodeURIComponent(slug)}/technical-sheet.pdf` : '',
    name,
    nameAr: found ? product.name : '',
    nameEn: found ? (product.nameEn ?? '') : '',
    slug: found ? product.slug : '',
    cartonUnits: found && Number.isInteger(product.cartonUnits) ? product.cartonUnits : '',
    category, description, wa, image,
    tone: found && TONES.has(product.tone) ? product.tone : 'gold',
  }
  const seo = seoHead({ lang, product: found ? product : null, base, title, description: metaDesc, imageAbs: image ? base + image : '' })

  const keep = found ? 'FOUND' : 'MISSING'
  let out = html.replace(/<!--P:(FOUND|MISSING)-->([\s\S]*?)<!--\/P:\1-->/g, (_, k, inner) => (k === keep ? inner : ''))
  out = out.replace('<!--P:SEO-->', () => seo)
  out = out.replace(/<!--P:SPECIFICATIONS-->[\s\S]*?<!--\/P:SPECIFICATIONS-->/, () => found ? buildConsumerSpecsMarkup(product, lang) : '')
  out = out.replace(/\[\[p:([\w.]+)\]\]/g, (_, k) => esc(values[k] ?? ''))
  out = out.replace(/\[\[t:([\w.]+)\]\]/g, (_, k) => esc(tr(lang, k, { name, category })))
  return out
}

// Public product detail routes: /products/:slug and /en/products/:slug.
export function registerProductPages(router, { frontendDir, siteBase }) {
  const templatePath = path.join(frontendDir, 'product.html')
  if (!fs.existsSync(templatePath)) return
  const render = createRenderer(templatePath)

  const page = (lang) => async (req, res) => {
    const slug = req.params.slug
    const product = SLUG.test(slug) && slug.length <= 150 ? await findPublicProductBySlug(slug) : null
    const base = siteBase(req)
    const html = fill(await render(lang, { baseUrl: base }), { lang, slug: SLUG.test(slug) ? slug : '', product, base })
    res.status(product ? 200 : 404).set({
      'Content-Type': 'text/html; charset=utf-8',
      'Cache-Control': 'no-cache',
      'Content-Language': lang,
    }).send(html)
  }

  router.get('/products/:slug', page('ar'))
  router.get('/en/products/:slug', page('en'))
  router.get('/products', (_req, res) => res.redirect(301, '/#products'))
  router.get('/en/products', (_req, res) => res.redirect(301, '/en#products'))
}
