import path from 'node:path'
import { fileURLToPath } from 'node:url'

const here = path.dirname(fileURLToPath(import.meta.url)) // backend/src/config

// جذر الواجهة وصور المنتجات المرفوعة (تُخدَم علنًا عبر express.static من /img/products/...)
export const FRONTEND_DIR = path.resolve(here, '../../../frontend')
export const IMG_DIR = path.join(FRONTEND_DIR, 'img')
export const PRODUCT_IMG_DIR = path.join(IMG_DIR, 'products')
export const PRODUCT_IMG_URL_PREFIX = '/img/products/'
