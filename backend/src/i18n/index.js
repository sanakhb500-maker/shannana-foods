import siteAr from './site.ar.js'
import siteEn from './site.en.js'
import { client, api } from './messages.js'

export const LANGS = ['ar', 'en']
export const DEFAULT_LANG = 'ar'
export const DIRS = { ar: 'rtl', en: 'ltr' }

const SITE = { ar: siteAr, en: siteEn }

// أي قيمة غير مدعومة ترجع إلى العربية (الافتراضية)
export const normalizeLang = (v) => (LANGS.includes(v) ? v : DEFAULT_LANG)
export const isLang = (v) => LANGS.includes(v)

export const siteDict = (lang) => SITE[normalizeLang(lang)]
export const clientDict = (lang) => client[normalizeLang(lang)]

// دالة ترجمة لرسائل الـ API: t('v.tooShort', { label: 'Name', min: 2 })
export function apiT(lang) {
  const dict = api[normalizeLang(lang)]
  return (key, vars = {}) => {
    const tpl = dict[key] ?? api[DEFAULT_LANG][key] ?? key
    return tpl.replace(/\{(\w+)\}/g, (_, k) => (k in vars ? String(vars[k]) : `{${k}}`))
  }
}

// اللغة المطلوبة في طلب API: من حقل lang في الجسم أو الاستعلام (وإلا العربية)
export const langOf = (req) => normalizeLang(req.body?.lang ?? req.query?.lang)
