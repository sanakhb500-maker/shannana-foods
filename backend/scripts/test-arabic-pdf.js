import { createRequire } from 'node:module';
import path from 'node:path';
import { stat } from 'node:fs/promises';

const require = createRequire(import.meta.url);

async function main() {
  const pdfMake = require('@digicole/pdfmake-rtl');

  // Use the package's documented font entry points (not internal build paths).
  const Roboto = require('@digicole/pdfmake-rtl/fonts/Roboto');
  const Cairo = require('@digicole/pdfmake-rtl/fonts/Cairo');
  pdfMake.addFonts(Roboto);
  pdfMake.addFonts(Cairo);

  const documentDefinition = {
    rtl: true,
    pageSize: 'A4',
    pageMargins: [42, 46, 42, 48],
    info: {
      title: 'Shannana Foods - Arabic PDF smoke test',
      subject: 'Arabic shaping and RTL verification',
      author: 'Shannana Foods',
    },
    content: [
      // Do not force font:'Cairo'/'Roboto' on individual nodes.
      // pdfmake-rtl detects the script and selects the matching registered font.
      { text: 'شنانة فودز', bold: true, fontSize: 22, alignment: 'right', margin: [0, 0, 0, 5] },
      { text: 'ورقة المواصفات الفنية', bold: true, fontSize: 17, alignment: 'right', margin: [0, 0, 0, 16] },
      { text: 'SHANNANA FOODS', bold: true, fontSize: 18, alignment: 'left', margin: [0, 0, 0, 4] },
      { text: 'PRODUCT TECHNICAL DATA SHEET', fontSize: 13, alignment: 'left', margin: [0, 0, 0, 18] },
      { text: 'اختبار عرض اللغة العربية واتصال الحروف', fontSize: 13, alignment: 'right', margin: [0, 0, 0, 5] },
      { text: 'Arabic shaping, RTL direction, and mixed-language layout test.', fontSize: 10, alignment: 'left', margin: [0, 0, 0, 14] },
      { text: 'معلومات المنتج', bold: true, fontSize: 14, alignment: 'right', margin: [0, 0, 0, 7] },
      { text: 'PRODUCT INFORMATION', bold: true, fontSize: 11, alignment: 'left', margin: [0, 0, 0, 7] },
      {
        table: {
          rtl: true,
          widths: ['*', '*'],
          body: [
            [ { text: 'اسم المنتج', bold: true, alignment: 'right' }, { text: 'كاتشب الطماطم الحلو', alignment: 'right' } ],
            [ { text: 'الوزن الصافي', bold: true, alignment: 'right' }, { text: '500 غرام', alignment: 'right' } ],
            [ { text: 'مدة الصلاحية', bold: true, alignment: 'right' }, { text: '12 شهرًا', alignment: 'right' } ],
          ],
        },
        layout: 'lightHorizontalLines',
        margin: [0, 0, 0, 14],
      },
      {
        table: {
          widths: ['*', '*'],
          body: [
            [ { text: 'Product', bold: true }, { text: 'Value', bold: true } ],
            [ 'Sweet Tomato Ketchup', 'Net content: 500 g' ],
            [ 'Barcode', '0123456789012' ],
          ],
        },
        layout: 'lightHorizontalLines',
      },
    ],
    defaultStyle: { fontSize: 10 },
    footer: (currentPage, pageCount) => ({
      text: `Shannana Foods | Page ${currentPage} of ${pageCount}`,
      fontSize: 8,
      alignment: 'center',
      margin: [0, 16, 0, 0],
    }),
  };

  const outputPath = path.resolve(process.cwd(), 'phase-e-arabic-pdf-smoke-test.pdf');
  const pdf = pdfMake.createPdf(documentDefinition);
  await pdf.write(outputPath);

  const fileInfo = await stat(outputPath);
  if (fileInfo.size < 1000) {
    throw new Error(`الملف الناتج صغير بشكل غير متوقع (${fileInfo.size} bytes).`);
  }

  console.log('تم إنشاء ملف PDF التجريبي بنجاح.');
  console.log(`المسار: ${outputPath}`);
  console.log(`الحجم: ${fileInfo.size.toLocaleString('en-US')} bytes`);
  console.log('افتح الملف وتحقق بصريًا من اتصال الحروف العربية واتجاهها والجداول والنص الإنجليزي.');
}

main().catch((error) => {
  console.error('فشل اختبار PDF:');
  console.error(error?.stack ?? error);
  process.exitCode = 1;
});
