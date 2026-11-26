import fs from 'node:fs/promises'
import { siteDict, clientDict, DIRS, LANGS, normalizeLang } from './index.js'

const esc = (v) => String(v).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c])
const TOKEN = /\{\{\{([\w.:-]+)\}\}\}|\{\{([\w.:-]+)\}\}/g

// مسار الصفحة الرئيسية لكل لغة (يتسع لاحقًا لصفحات المنتجات: /products/<slug> و/en/products/<slug>)
export const homePath = (lang) => (lang === 'en' ? '/en' : '/')

const OG_LOCALE = { ar: 'ar_AR', en: 'en_US' }

function seoHead(lang, d, baseUrl) {
  const url = (l) => baseUrl + (l === 'en' ? '/en' : '/')
  const alt = LANGS.find((l) => l !== lang)
  return [
    `<title>${esc(d['meta.title'])}</title>`,
    `<meta name="description" content="${esc(d['meta.description'])}" />`,
    `<link rel="canonical" href="${esc(url(lang))}" />`,
    ...LANGS.map((l) => `<link rel="alternate" hreflang="${l}" href="${esc(url(l))}" />`),
    `<link rel="alternate" hreflang="x-default" href="${esc(url('ar'))}" />`,
    `<meta property="og:type" content="website" />`,
    `<meta property="og:site_name" content="Shannana Foods" />`,
    `<meta property="og:title" content="${esc(d['meta.title'])}" />`,
    `<meta property="og:description" content="${esc(d['meta.description'])}" />`,
    `<meta property="og:url" content="${esc(url(lang))}" />`,
    `<meta property="og:locale" content="${OG_LOCALE[lang]}" />`,
    `<meta property="og:locale:alternate" content="${OG_LOCALE[alt]}" />`,
    `<meta property="og:image" content="${esc(baseUrl + '/img/HeroProduct.png')}" />`,
  ].join('\n  ')
}

// عارض قالب بسيط: {{key}} (مُهرَّب) و{{{key}}} (HTML موثوق) و{{url:key}} (مُرمَّز للروابط).
// يعيد قراءة القالب عند تغيّر الملف (مناسب للتطوير) ويحفظه في الذاكرة غير ذلك.
export function createRenderer(templatePath) {
  let cached = { mtimeMs: 0, text: '' }
  async function template() {
    const st = await fs.stat(templatePath)
    if (st.mtimeMs !== cached.mtimeMs) cached = { mtimeMs: st.mtimeMs, text: await fs.readFile(templatePath, 'utf8') }
    return cached.text
  }
  return async function render(langInput, { baseUrl }) {
    const lang = normalizeLang(langInput)
    const d = siteDict(lang)
    const text = await template()
    const alt = LANGS.find((l) => l !== lang)
    const computed = {
      lang,
      dir: DIRS[lang],
      'head.seo': seoHead(lang, d, baseUrl),
      'client.json': JSON.stringify(clientDict(lang)).replace(/</g, '\\u003c'),
      'lang.href.ar': homePath('ar'),
      'lang.href.en': homePath('en'),
      'lang.cls.ar': lang === 'ar' ? ' active' : '',
      'lang.cls.en': lang === 'en' ? ' active' : '',
      'lang.cur.ar': lang === 'ar' ? ' aria-current="true"' : '',
      'lang.cur.en': lang === 'en' ? ' aria-current="true"' : '',
      'lang.alt': alt,
    }
    const missing = []
    const html = text.replace(TOKEN, (_, rawKey, escKey) => {
      const key = rawKey || escKey
      if (key.startsWith('url:')) {
        const v = d[key.slice(4)]
        if (v === undefined) { missing.push(key); return '' }
        return encodeURIComponent(v)
      }
      const v = key in computed ? computed[key] : d[key]
      if (v === undefined) { missing.push(key); return '' }
      return rawKey ? String(v) : esc(v)
    })
    if (missing.length) console.warn(`[i18n] مفاتيح ترجمة ناقصة (${lang}): ${[...new Set(missing)].join(', ')}`)
    return html
  }
}
