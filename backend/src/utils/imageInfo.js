// كشف نوع الصورة من محتواها الفعلي (magic bytes) واستخراج أبعادها بدون فك ترميز كامل.
// يدعم PNG وJPEG وWebP فقط. أي شيء آخر (SVG, GIF, HTML, ...) يعيد null.

function png(b) {
  if (b.length < 33 || b.readUInt32BE(0) !== 0x89504e47 || b.readUInt32BE(4) !== 0x0d0a1a0a) return null
  if (b.toString('ascii', 12, 16) !== 'IHDR') return null
  if (b.lastIndexOf('IEND') < 33) return null // ملف مبتور
  return { type: 'image/png', ext: 'png', width: b.readUInt32BE(16), height: b.readUInt32BE(20) }
}

const SOF = new Set([0xc0, 0xc1, 0xc2, 0xc3, 0xc5, 0xc6, 0xc7, 0xc9, 0xca, 0xcb, 0xcd, 0xce, 0xcf])
function jpeg(b) {
  if (b.length < 4 || b[0] !== 0xff || b[1] !== 0xd8 || b[2] !== 0xff) return null
  if (b.lastIndexOf(Buffer.from([0xff, 0xd9])) < 2) return null // ملف مبتور
  let i = 2
  while (i + 4 < b.length) {
    if (b[i] !== 0xff) { i++; continue }
    const marker = b[i + 1]
    if (marker === 0xff) { i++; continue }
    if (marker === 0xd8 || marker === 0x01 || (marker >= 0xd0 && marker <= 0xd7)) { i += 2; continue }
    const len = b.readUInt16BE(i + 2)
    if (len < 2) return null
    if (SOF.has(marker)) {
      if (i + 9 > b.length) return null
      return { type: 'image/jpeg', ext: 'jpg', height: b.readUInt16BE(i + 5), width: b.readUInt16BE(i + 7) }
    }
    i += 2 + len
  }
  return null
}

function webp(b) {
  if (b.length < 30 || b.toString('ascii', 0, 4) !== 'RIFF' || b.toString('ascii', 8, 12) !== 'WEBP') return null
  if (b.readUInt32LE(4) + 8 > b.length) return null // ملف مبتور
  const kind = b.toString('ascii', 12, 16)
  let width, height
  if (kind === 'VP8 ') {
    if (b[23] !== 0x9d || b[24] !== 0x01 || b[25] !== 0x2a) return null
    width = b.readUInt16LE(26) & 0x3fff
    height = b.readUInt16LE(28) & 0x3fff
  } else if (kind === 'VP8L') {
    if (b[20] !== 0x2f) return null
    width = 1 + (((b[22] & 0x3f) << 8) | b[21])
    height = 1 + (((b[24] & 0x0f) << 10) | (b[23] << 2) | ((b[22] & 0xc0) >> 6))
  } else if (kind === 'VP8X') {
    width = 1 + (b[24] | (b[25] << 8) | (b[26] << 16))
    height = 1 + (b[27] | (b[28] << 8) | (b[29] << 16))
  } else return null
  return { type: 'image/webp', ext: 'webp', width, height }
}

export function sniffImage(buffer) {
  return png(buffer) || jpeg(buffer) || webp(buffer)
}
