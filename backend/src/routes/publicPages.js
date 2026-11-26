import fs from 'node:fs'
import path from 'node:path'
import { Router } from 'express'
import { createRenderer } from '../i18n/render.js'
import { LANGS } from '../i18n/index.js'
import { registerProductPages, productPath } from './productPages.js'
import { listSitemapProducts } from '../services/productPage.service.js'

// عنوان الموقع المطلق (canonical / hreflang / sitemap). الأفضل ضبط SITE_URL في .env (مثل https://shannana-foods.com)
// وإلا يُشتق من الطلب. القيمة تُتحقق: http/https فقط وبدون مسار.
function siteBase(req) {
  const fromEnv = process.env.SITE_URL?.trim()
  if (fromEnv) {
    try {
      const u = new URL(fromEnv)
      if (u.protocol === 'http:' || u.protocol === 'https:') return u.origin
    } catch { /* نعود للمشتق من الطلب */ }
  }
  return `${req.protocol}://${req.host}`
}

// الصفحات العامة ثنائية اللغة. العربية على /، والإنجليزية على /en.
// المسارات الثابتة (/css /js /img) والإدارة (/admin) و/api لا تتأثر.
export function createPublicPages(frontendDir) {
  const router = Router()
  const templatePath = path.join(frontendDir, 'index.html')
  if (!fs.existsSync(templatePath)) return router
  const render = createRenderer(templatePath)
  registerProductPages(router, { frontendDir, siteBase }) // /products/:slug و/en/products/:slug (PHASE 11A)

  const home = (lang) => async (req, res) => {
    const html = await render(lang, { baseUrl: siteBase(req) })
    res.set({ 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-cache', 'Content-Language': lang }).send(html)
  }

  router.get('/', home('ar'))
  router.get('/en', home('en')) // /en/ يعمل أيضًا (التوجيه غير صارم)
  router.get('/index.html', (_req, res) => res.redirect(301, '/'))
  router.get('/en/index.html', (_req, res) => res.redirect(301, '/en'))

  router.get('/robots.txt', (req, res) => {
    res.type('text/plain').send(`User-agent: *\nDisallow: /admin\nDisallow: /api/\n\nSitemap: ${siteBase(req)}/sitemap.xml\n`)
  })

  router.get('/sitemap.xml', async (req, res) => {
    const base = siteBase(req)
    const entry = (pathOf, lastmod) => {
      const loc = (l) => base + pathOf(l)
      const links = LANGS.map((l) => `    <xhtml:link rel="alternate" hreflang="${l}" href="${loc(l)}"/>`).join('\n') + `\n    <xhtml:link rel="alternate" hreflang="x-default" href="${loc('ar')}"/>`
      return LANGS.map((l) => `  <url>\n    <loc>${loc(l)}</loc>\n${lastmod ? `    <lastmod>${lastmod}</lastmod>\n` : ''}${links}\n  </url>`).join('\n')
    }
    const products = await listSitemapProducts() // المنتجات الظاهرة فقط
    const urls = [entry((l) => (l === 'en' ? '/en' : '/')), ...products.map((p) => entry((l) => productPath(l, p.slug), p.updatedAt.toISOString().slice(0, 10)))].join('\n')
    res.type('application/xml').send(`<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">\n${urls}\n</urlset>\n`)
  })

  return router
}
