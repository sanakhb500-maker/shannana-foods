// Shared admin UI for inquiries and distribution requests. Text from visitors is rendered with textContent only.
(() => {
  const A = window.Admin
  const $ = A.$
  const kind = document.body.dataset.requestKind
  const isInquiry = kind === 'inquiries'
  const endpoint = isInquiry ? '/admin/inquiries' : '/admin/distribution-requests'
  const statuses = isInquiry
    ? [['new', 'جديد'], ['contacted', 'تم التواصل'], ['closed', 'مغلق']]
    : [['new', 'جديد'], ['contacted', 'تم التواصل'], ['qualified', 'مؤهل'], ['closed', 'مغلق']]
  const businessTypes = { shop: 'محل', distributor: 'موزع', wholesaler: 'تاجر جملة', restaurant: 'مطعم', supermarket: 'سوبرماركت', other: 'أخرى' }
  let page = 1
  const pageSize = 20
  let totalPages = 1
  let activeLoad = false

  function cell(label, value, className = '') {
    const td = A.h('td', { 'data-label': label, class: className })
    if (value && value.nodeType) td.append(value)
    else td.textContent = value == null || value === '' ? '—' : String(value)
    return td
  }
  function contactCell(row) {
    const wrap = A.h('div', { class: 'request-contact' })
    if (row.phone) wrap.append(A.h('div', { text: `هاتف: ${row.phone}` }))
    if (row.whatsapp) wrap.append(A.h('div', { text: `واتساب: ${row.whatsapp}` }))
    if (row.email) wrap.append(A.h('div', { dir: 'ltr', text: row.email }))
    if (!wrap.childNodes.length) wrap.textContent = '—'
    return wrap
  }
  function displayDate(raw) {
    const date = new Date(raw)
    return Number.isNaN(date.getTime()) ? '—' : date.toLocaleString('ar-SY', { dateStyle: 'medium', timeStyle: 'short' })
  }
  function statusCell(row) {
    const wrap = A.h('div', { class: 'request-status' })
    const label = statuses.find(([value]) => value === row.status)?.[1] || row.status
    const badgeClass = row.status === 'closed' ? 'badge off' : row.status === 'new' ? 'badge warn' : 'badge on'
    wrap.append(A.h('span', { class: badgeClass, text: label }))
    const select = A.h('select', { class: 'status-select', 'aria-label': `الحالة الجديدة للسجل ${row.id}` })
    statuses.forEach(([value, text]) => select.append(A.h('option', { value, text, selected: value === row.status })))
    const save = A.h('button', {
      type: 'button', class: 'btn btn-ghost sm', text: 'حفظ الحالة',
      onclick: async () => {
        if (select.value === row.status) { A.toast('الحالة لم تتغير', 'ok'); return }
        save.disabled = true
        try {
          await A.api(`${endpoint}/${row.id}`, { method: 'PATCH', json: { status: select.value } })
          A.toast('تم تحديث الحالة بنجاح')
          await load()
        } catch (err) {
          A.toast(err.message || 'تعذّر تحديث الحالة', 'err')
          save.disabled = false
        }
      },
    })
    const editor = A.h('div', { class: 'status-editor' }, select, save)
    wrap.append(editor)
    return wrap
  }
  function renderRow(row) {
    const tr = A.h('tr')
    if (isInquiry) {
      const name = A.h('div', { class: 'cell-main' }, A.h('strong', { text: row.name || '—' }), A.h('span', { class: 'slug', text: `#${row.id}` }))
      const topic = row.product?.name ? `المنتج: ${row.product.name}` : (row.source === 'whatsapp' ? 'واتساب' : 'استفسار عام')
      tr.append(
        cell('صاحب الاستفسار', name, 'cell-main'),
        cell('التواصل', contactCell(row)),
        cell('عن ماذا؟', topic),
        cell('الرسالة', A.h('div', { class: 'request-message', text: row.message || '—' })),
        cell('تاريخ الإرسال', displayDate(row.createdAt)),
        cell('الحالة', statusCell(row)),
        cell('إجراءات', A.h('span', { class: 'request-id', text: `رقم الاستفسار: ${row.id}` }), 'actions'),
      )
    } else {
      const name = A.h('div', { class: 'cell-main' }, A.h('strong', { text: row.name || '—' }), A.h('span', { class: 'slug', text: `#${row.id}` }))
      const business = A.h('div', { class: 'cell-main' }, A.h('strong', { text: row.businessName || '—' }), row.message ? A.h('span', { class: 'slug request-message', text: row.message }) : null)
      const location = [row.city, row.area].filter(Boolean).join(' — ') || '—'
      tr.append(
        cell('الشخص المسؤول', name, 'cell-main'),
        cell('المنشأة', business, 'cell-main'),
        cell('التواصل', contactCell(row)),
        cell('المدينة / المنطقة', location),
        cell('نوع النشاط', businessTypes[row.businessType] || row.businessType || '—'),
        cell('تاريخ الإرسال', displayDate(row.createdAt)),
        cell('الحالة', statusCell(row)),
        cell('إجراءات', A.h('span', { class: 'request-id', text: `رقم الطلب: ${row.id}` }), 'actions'),
      )
    }
    return tr
  }
  async function load() {
    if (activeLoad) return
    activeLoad = true
    $('loading').hidden = false
    $('empty').hidden = true
    $('pageError').hidden = true
    $('rows').replaceChildren()
    $('count').textContent = 'جارٍ التحميل…'
    $('searchBtn').disabled = true
    try {
      const query = new URLSearchParams({
        status: $('statusFilter').value,
        page: String(page),
        pageSize: String(pageSize),
      })
      const search = $('search').value.trim()
      if (search) query.set('search', search)
      const result = await A.api(`${endpoint}?${query.toString()}`)
      const rows = Array.isArray(result.data) ? result.data : []
      const meta = result.meta || { page, pageSize, total: rows.length, totalPages: 1 }
      totalPages = Math.max(1, Number(meta.totalPages) || 1)
      rows.forEach((row) => $('rows').append(renderRow(row)))
      $('empty').hidden = rows.length > 0
      $('count').textContent = `${Number(meta.total) || 0} سجل`
      $('pageInfo').textContent = `الصفحة ${meta.page || page} من ${totalPages}`
      $('prevPage').disabled = page <= 1
      $('nextPage').disabled = page >= totalPages
    } catch (err) {
      $('pageError').textContent = err.message || 'تعذّر تحميل البيانات. حاول مجددًا.'
      $('pageError').hidden = false
      $('count').textContent = ''
    } finally {
      $('loading').hidden = true
      $('searchBtn').disabled = false
      activeLoad = false
    }
  }

  $('searchBtn').addEventListener('click', () => { page = 1; load() })
  $('search').addEventListener('keydown', (event) => {
    if (event.key === 'Enter') { event.preventDefault(); page = 1; load() }
  })
  $('statusFilter').addEventListener('change', () => { page = 1; load() })
  $('prevPage').addEventListener('click', () => { if (page > 1) { page -= 1; load() } })
  $('nextPage').addEventListener('click', () => { if (page < totalPages) { page += 1; load() } })
  A.initShell().then(load).catch((err) => {
    $('pageError').textContent = err.message || 'تعذّر تهيئة لوحة الإدارة.'
    $('pageError').hidden = false
    $('loading').hidden = true
  })
})()
