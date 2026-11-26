import { writeFile, readFile } from 'node:fs/promises'
import path from 'node:path'
import { buildTechnicalSheetDefinition, generateTechnicalSheetPdfBuffer } from '../src/services/technicalSheetPdf.service.js'

const sampleProduct = {
  id: 999999,
  name: 'كاتشب الطماطم الحلو',
  nameEn: 'Sweet Tomato Ketchup',
  slug: 'sweet-tomato-ketchup-test',
  description: 'صلصة طماطم حلوة للاستخدام اليومي.',
  descriptionEn: 'Sweet tomato sauce for everyday use.',
  netContent: '500.000',
  netContentUnit: 'g',
  packType: 'عبوة بلاستيكية',
  packTypeEn: 'Plastic bottle',
  packMaterial: 'بلاستيك',
  packMaterialEn: 'Plastic',
  barcode: '0123456789012',
  cartonUnits: 12,
  cartonLengthCm: '30.00',
  cartonWidthCm: '20.00',
  cartonHeightCm: '15.00',
  cartonNetWeightKg: '6.000',
  cartonGrossWeightKg: '6.400',
  shelfLifeValue: 12,
  shelfLifeUnit: 'month',
  storageNote: 'يحفظ في مكان بارد وجاف.',
  storageNoteEn: 'Store in a cool, dry place.',
  originCountry: 'SY',
  hsCode: '21032000',
  palletCartons: 80,
  palletCartonsPerLayer: 10,
  exportNote: 'بيانات اختبار فقط، لا تستخدم تجاريًا.',
  exportNoteEn: 'Test data only; not for commercial use.',
}

async function saveAndCheck(filename, audience) {
  const buffer = await generateTechnicalSheetPdfBuffer(sampleProduct, { audience })
  const outputPath = path.resolve(process.cwd(), filename)
  await writeFile(outputPath, buffer)

  const saved = await readFile(outputPath)
  if (saved.subarray(0, 5).toString('ascii') !== '%PDF-' || saved.length < 1000) {
    throw new Error(`PDF ${filename} failed validation.`)
  }
  console.log(`${filename}: OK (${saved.length.toLocaleString('en-US')} bytes)`)
  return saved
}

try {
  const adminDefinition = JSON.stringify(buildTechnicalSheetDefinition(sampleProduct, { audience: 'admin' }))
  const publicDefinition = JSON.stringify(buildTechnicalSheetDefinition(sampleProduct, { audience: 'public' }))

  if (!adminDefinition.includes(sampleProduct.barcode) || !adminDefinition.includes(sampleProduct.hsCode)) {
    throw new Error('Admin PDF definition is missing expected barcode or HS code fields.')
  }
  for (const privateValue of [sampleProduct.barcode, sampleProduct.hsCode, sampleProduct.exportNote, sampleProduct.exportNoteEn]) {
    if (publicDefinition.includes(privateValue)) {
      throw new Error('Public PDF definition unexpectedly contains an admin-only commercial field.')
    }
  }

  await saveAndCheck('phase-e-admin-technical-sheet-preview.pdf', 'admin')
  await saveAndCheck('phase-e-public-technical-sheet-preview.pdf', 'public')

  console.log('نجح اختبار إنشاء نسختي الإدارة والعامة. افتح الملفين وتحقق بصريًا من العربية والجداول.')
} catch (error) {
  console.error('فشل اختبار Technical Sheet PDF:')
  console.error(error?.stack ?? error)
  process.exitCode = 1
}
