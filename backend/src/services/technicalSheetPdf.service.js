import { createRequire } from 'node:module'
import { mkdtemp, readFile, rm } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'

const require = createRequire(import.meta.url)
const pdfMake = require('@digicole/pdfmake-rtl')

// Register the same documented font bundles used by the successful E1 smoke test.
pdfMake.addFonts(require('@digicole/pdfmake-rtl/fonts/Roboto'))
pdfMake.addFonts(require('@digicole/pdfmake-rtl/fonts/Cairo'))

const BRAND = {
  burgundy: '#A8202C',
  burgundyDark: '#771923',
  olive: '#718C3F',
  ink: '#292524',
  muted: '#6B7280',
  border: '#E7E2DA',
  pale: '#FAF7F1',
  white: '#FFFFFF',
}

const CONTENT_UNITS = {
  g: { ar: 'غرام', en: 'g' },
  kg: { ar: 'كيلوغرام', en: 'kg' },
  ml: { ar: 'مل', en: 'mL' },
  l: { ar: 'لتر', en: 'L' },
}

const TIME_UNITS = {
  day: { ar: 'يوم', en: 'day' },
  month: { ar: 'شهر', en: 'month' },
  year: { ar: 'سنة', en: 'year' },
}

const hasValue = (value) =>
  value !== null && value !== undefined && String(value).trim() !== ''

// Preserve Prisma Decimal precision; never convert specification values to Number.
function decimalText(value) {
  if (!hasValue(value)) return ''
  return String(value).replace(/(\.\d*?[1-9])0+$/, '$1').replace(/\.0+$/, '')
}

// Represent an Arabic measurement as two independent text columns.
// pdfmake-rtl can otherwise classify a digits-only inline as neutral inside an
// RTL paragraph and reorder its digits. Keeping the amount in its own explicit
// LTR text node preserves Western digit order; the unit remains an RTL node.
function arabicMeasurement(amount, unit) {
  return {
    __kind: 'arabic-measurement',
    amount: String(amount),
    unit: String(unit),
  }
}

function isArabicMeasurement(value) {
  return Boolean(value && typeof value === 'object' && value.__kind === 'arabic-measurement')
}

function plainText(value) {
  return typeof value === 'string' ? value.trim() : ''
}

function countryNames(code) {
  const normalized = plainText(code).toUpperCase()
  if (!/^[A-Z]{2}$/.test(normalized)) return { ar: '', en: '' }
  if (normalized === 'SY') return { ar: 'سوريا', en: 'Syria' }

  const getName = (locale) => {
    try {
      const value = new Intl.DisplayNames([locale], { type: 'region' }).of(normalized)
      return value && value !== normalized ? value : normalized
    } catch {
      return normalized
    }
  }

  return { ar: getName('ar'), en: getName('en') }
}

function formatTimeUnit(value, unit) {
  if (!hasValue(value) || !TIME_UNITS[unit]) return { ar: '', en: '' }
  const amount = String(value).trim()
  const names = TIME_UNITS[unit]
  return {
    ar: arabicMeasurement(amount, names.ar),
    en: `${amount} ${names.en}${amount === '1' ? '' : 's'}`,
  }
}

function bilingualValue(ar, en) {
  return { ar: plainText(ar), en: plainText(en) }
}

function createRow(labelAr, labelEn, valueAr, valueEn = valueAr) {
  const structuredArabic = isArabicMeasurement(valueAr) ? valueAr : null
  const arValue = structuredArabic ? '' : (hasValue(valueAr) ? String(valueAr).trim() : '')
  const hasArabicValue = Boolean(structuredArabic || arValue)
  const enValue = hasValue(valueEn) ? String(valueEn).trim() : ''
  if (!hasArabicValue && !enValue) return null

  const arabicValueNode = structuredArabic
    ? {
        // The flexible spacer anchors the amount+unit pair to the right edge.
        // Columns are ordered left-to-right: amount on the left, Arabic unit on the right.
        columns: [
          { width: '*', text: '' },
          {
            width: 'auto',
            text: structuredArabic.amount,
            font: 'Roboto',
            direction: 'ltr',
            alignment: 'left',
            noWrap: true,
            fontSize: 9.5,
            color: BRAND.ink,
          },
          {
            width: 'auto',
            text: structuredArabic.unit,
            font: 'Cairo',
            direction: 'rtl',
            alignment: 'right',
            noWrap: true,
            fontSize: 9.5,
            color: BRAND.ink,
          },
        ],
        columnGap: 4,
      }
    : (arValue ? { text: arValue, fontSize: 9.5, alignment: 'right', color: BRAND.ink } : null)

  return [
    {
      stack: [
        { text: labelAr, bold: true, fontSize: 9, alignment: 'right', color: BRAND.ink },
        { text: labelEn, fontSize: 8, alignment: 'left', color: BRAND.muted, margin: [0, 2, 0, 0] },
      ],
      fillColor: BRAND.pale,
      margin: [7, 7, 7, 7],
    },
    {
      stack: [
        ...(arabicValueNode ? [arabicValueNode] : []),
        ...(enValue ? [{ text: enValue, fontSize: 8.5, alignment: 'left', color: BRAND.muted, margin: [0, hasArabicValue ? 3 : 0, 0, 0] }] : []),
      ],
      margin: [7, 7, 7, 7],
    },
  ]
}

function sectionBlock(titleAr, titleEn, rows) {
  const usableRows = rows.filter(Boolean)
  if (!usableRows.length) return null

  // Keep the colored section heading with at least the first data row.
  // If the remaining space is too small, pdfmake moves both to the next page.
  return {
    table: {
      headerRows: 1,
      keepWithHeaderRows: 1,
      dontBreakRows: true,
      widths: [145, '*'],
      body: [
        [
          {
            colSpan: 2,
            stack: [
              { text: titleAr, bold: true, fontSize: 12, alignment: 'right', color: BRAND.white },
              { text: titleEn, bold: true, fontSize: 9, alignment: 'left', color: BRAND.white, margin: [0, 2, 0, 0] },
            ],
            fillColor: BRAND.burgundy,
            margin: [10, 8, 10, 8],
          },
          {},
        ],
        ...usableRows,
      ],
    },
    layout: {
      hLineWidth: (index) => (index <= 1 ? 0 : 0.5),
      vLineWidth: () => 0,
      hLineColor: () => BRAND.border,
      paddingLeft: () => 0,
      paddingRight: () => 0,
      paddingTop: () => 0,
      paddingBottom: () => 0,
    },
    margin: [0, 12, 0, 0],
  }
}

function buildSections(product, audience) {
  const admin = audience === 'admin'
  const content = []

  const productRows = [
    createRow('اسم المنتج', 'Product Name', product.name, product.nameEn),
    createRow('الوصف', 'Description', product.description, product.descriptionEn),
  ]

  if (hasValue(product.netContent) && CONTENT_UNITS[product.netContentUnit]) {
    const amount = decimalText(product.netContent)
    const unit = CONTENT_UNITS[product.netContentUnit]
    productRows.push(createRow('المحتوى الصافي', 'Net Content', arabicMeasurement(amount, unit.ar), `${amount} ${unit.en}`))
  }

  productRows.push(
    createRow('نوع العبوة', 'Pack Type', product.packType, product.packTypeEn),
    createRow('مادة العبوة', 'Pack Material', product.packMaterial, product.packMaterialEn),
  )

  if (!admin && hasValue(product.originCountry)) {
    const country = countryNames(product.originCountry)
    productRows.push(createRow('بلد المنشأ', 'Country of Origin', country.ar, country.en))
  }

  const productSection = sectionBlock('معلومات المنتج', 'PRODUCT INFORMATION', productRows)
  if (productSection) content.push(productSection)

  const storageRows = []
  if (hasValue(product.shelfLifeValue) && TIME_UNITS[product.shelfLifeUnit]) {
    const time = formatTimeUnit(product.shelfLifeValue, product.shelfLifeUnit)
    storageRows.push(createRow('مدة الصلاحية', 'Shelf Life', time.ar, time.en))
  }
  storageRows.push(createRow('تعليمات التخزين', 'Storage Instructions', product.storageNote, product.storageNoteEn))

  const storageSection = sectionBlock('الصلاحية والتخزين', 'SHELF LIFE & STORAGE', storageRows)
  if (storageSection) content.push(storageSection)

  // The public PDF deliberately follows the public product-page specification allowlist.
  // Commercial packing, barcode, HS, pallet and export-note fields stay admin-only.
  if (admin) {
    const cartonRows = [
      createRow('عدد الوحدات في الكرتون', 'Units per Carton', hasValue(product.cartonUnits) ? String(product.cartonUnits) : '', hasValue(product.cartonUnits) ? String(product.cartonUnits) : ''),
      createRow('طول الكرتون', 'Carton Length', hasValue(product.cartonLengthCm) ? arabicMeasurement(decimalText(product.cartonLengthCm), 'سم') : '', hasValue(product.cartonLengthCm) ? `${decimalText(product.cartonLengthCm)} cm` : ''),
      createRow('عرض الكرتون', 'Carton Width', hasValue(product.cartonWidthCm) ? arabicMeasurement(decimalText(product.cartonWidthCm), 'سم') : '', hasValue(product.cartonWidthCm) ? `${decimalText(product.cartonWidthCm)} cm` : ''),
      createRow('ارتفاع الكرتون', 'Carton Height', hasValue(product.cartonHeightCm) ? arabicMeasurement(decimalText(product.cartonHeightCm), 'سم') : '', hasValue(product.cartonHeightCm) ? `${decimalText(product.cartonHeightCm)} cm` : ''),
      createRow('الوزن الصافي للكرتون', 'Carton Net Weight', hasValue(product.cartonNetWeightKg) ? arabicMeasurement(decimalText(product.cartonNetWeightKg), 'كغ') : '', hasValue(product.cartonNetWeightKg) ? `${decimalText(product.cartonNetWeightKg)} kg` : ''),
      createRow('الوزن الإجمالي للكرتون', 'Carton Gross Weight', hasValue(product.cartonGrossWeightKg) ? arabicMeasurement(decimalText(product.cartonGrossWeightKg), 'كغ') : '', hasValue(product.cartonGrossWeightKg) ? `${decimalText(product.cartonGrossWeightKg)} kg` : ''),
    ]
    const cartonSection = sectionBlock('مواصفات الكرتون', 'CARTON SPECIFICATIONS', cartonRows)
    if (cartonSection) content.push(cartonSection)

    const country = countryNames(product.originCountry)
    const exportRows = [
      createRow('الباركود', 'Barcode', product.barcode, product.barcode),
      createRow('بلد المنشأ', 'Country of Origin', country.ar, country.en),
      createRow('رمز النظام المنسق', 'HS Code', product.hsCode, product.hsCode),
    ]
    const exportSection = sectionBlock('الباركود والتصدير', 'BARCODE & EXPORT', exportRows)
    if (exportSection) content.push(exportSection)

    const palletRows = [
      createRow('عدد الكراتين على الطبلية', 'Cartons per Pallet', product.palletCartons, product.palletCartons),
      createRow('عدد الكراتين في الطبقة', 'Cartons per Layer', product.palletCartonsPerLayer, product.palletCartonsPerLayer),
    ]
    const palletSection = sectionBlock('معلومات الطبالي', 'PALLET INFORMATION', palletRows)
    if (palletSection) content.push(palletSection)

    const notesSection = sectionBlock('ملاحظات التصدير', 'EXPORT NOTES', [
      createRow('ملاحظات إضافية', 'Additional Notes', product.exportNote, product.exportNoteEn),
    ])
    if (notesSection) content.push(notesSection)
  }

  return content
}

export function buildTechnicalSheetDefinition(product, { audience = 'public' } = {}) {
  if (!product || !['public', 'admin'].includes(audience)) {
    throw new TypeError('بيانات المنتج أو نوع ورقة المواصفات غير صالح.')
  }

  const createdDate = new Date().toISOString().slice(0, 10)
  const name = plainText(product.name) || 'Product'
  const nameEn = plainText(product.nameEn)

  const content = [
    { text: 'شنانة فودز', bold: true, fontSize: 20, alignment: 'right', color: BRAND.burgundy, margin: [0, 0, 0, 2] },
    { text: 'SHANNANA FOODS', bold: true, fontSize: 16, alignment: 'left', color: BRAND.burgundy, margin: [0, 0, 0, 9] },
    { text: 'ورقة المواصفات الفنية', bold: true, fontSize: 17, alignment: 'right', color: BRAND.ink, margin: [0, 0, 0, 2] },
    { text: 'PRODUCT TECHNICAL DATA SHEET', bold: true, fontSize: 11, alignment: 'left', color: BRAND.ink, margin: [0, 0, 0, 12] },
    { text: name, bold: true, fontSize: 15, alignment: 'right', color: BRAND.ink, margin: [0, 0, 0, 2] },
    ...(nameEn ? [{ text: nameEn, bold: true, fontSize: 12, alignment: 'left', color: BRAND.muted, margin: [0, 0, 0, 8] }] : []),
    {
      columns: [
        { text: `تاريخ الإصدار: ${createdDate}`, fontSize: 8, color: BRAND.muted, alignment: 'right' },
        { text: `Issue Date: ${createdDate}`, fontSize: 8, color: BRAND.muted, alignment: 'left' },
      ],
      columnGap: 8,
      margin: [0, 0, 0, 5],
    },
    ...buildSections(product, audience),
  ]

  return {
    rtl: true,
    pageSize: 'A4',
    pageMargins: [40, 46, 40, 48],
    info: {
      title: `Shannana Foods - Product Technical Data Sheet - ${nameEn || name}`,
      subject: 'Bilingual product technical data sheet',
      author: 'Shannana Foods',
      creator: 'Shannana Foods website',
    },
    header: (currentPage) => currentPage > 1 ? ({
      text: 'SHANNANA FOODS  |  PRODUCT TECHNICAL DATA SHEET',
      fontSize: 7,
      color: BRAND.muted,
      alignment: 'left',
      margin: [40, 18, 40, 0],
    }) : { text: '' },
    content,
    defaultStyle: { fontSize: 9 },
    footer: (currentPage, pageCount) => ({
      columns: [
        { text: `Shannana Foods | Page ${currentPage} of ${pageCount}`, fontSize: 7.5, color: BRAND.muted, alignment: 'left' },
        { text: `شنانة فودز | الصفحة ${currentPage} من ${pageCount}`, fontSize: 7.5, color: BRAND.muted, alignment: 'right' },
      ],
      margin: [40, 14, 40, 0],
    }),
  }
}

function safeFileSlug(value) {
  const slug = String(value ?? '').normalize('NFKD').toLowerCase().replace(/[^a-z0-9-]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 90)
  return slug || 'product'
}

export function technicalSheetFilename(product) {
  return `technical-sheet-${safeFileSlug(product?.slug)}.pdf`
}

export async function generateTechnicalSheetPdfBuffer(product, options = {}) {
  const definition = buildTechnicalSheetDefinition(product, options)
  const pdf = pdfMake.createPdf(definition)

  // The installed package's write() API is the exact output path exercised by E1.
  // Use an isolated OS temp directory and always remove it; PDFs are never kept in the app.
  const tempDir = await mkdtemp(path.join(os.tmpdir(), 'shannana-technical-sheet-'))
  const tempFile = path.join(tempDir, 'technical-sheet.pdf')

  try {
    await pdf.write(tempFile)
    const buffer = await readFile(tempFile)
    if (buffer.length < 1000 || buffer.subarray(0, 5).toString('ascii') !== '%PDF-') {
      throw new Error('تعذر إنشاء ملف PDF صالح لورقة المواصفات الفنية.')
    }
    return buffer
  } finally {
    await rm(tempDir, { recursive: true, force: true }).catch(() => {})
  }
}
