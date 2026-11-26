/* Renders the latest real inquiry and distribution records on the admin dashboard. */
(() => {
  const STATUS_LABELS = {
    new: 'جديد',
    contacted: 'تم التواصل',
    qualified: 'مؤهل',
    closed: 'مغلق',
  }
  const BUSINESS_TYPES = {
    shop: 'محل',
    distributor: 'موزع',
    wholesaler: 'تاجر جملة',
    restaurant: 'مطعم',
    supermarket: 'سوبرماركت',
    other: 'أخرى',
  }

  const $ = (id) => document.getElementById(id)
  const dateFormatter = new Intl.DateTimeFormat('ar-SY', { dateStyle: 'medium', timeStyle: 'short' })

  function el(tag, className, text) {
    const node = document.createElement(tag)
    if (className) node.className = className
    if (text !== undefined && text !== null) node.textContent = String(text)
    return node
  }

  function dateText(value) {
    const date = new Date(value)
    return Number.isNaN(date.getTime()) ? 'التاريخ غير متوفر' : dateFormatter.format(date)
  }

  function shortText(value, max = 155) {
    const text = String(value || '').trim()
    return text.length > max ? `${text.slice(0, max).trimEnd()}…` : text
  }

  async function getRows(path) {
    let response
    try {
      response = await fetch(`/api/admin/${path}?status=all&page=1&pageSize=3`, {
        method: 'GET',
        credentials: 'same-origin',
        headers: { Accept: 'application/json' },
        cache: 'no-store',
      })
    } catch {
      throw new Error('تعذّر الاتصال بالخادم.')
    }

    if (response.status === 401) {
      window.location.replace('/admin/login')
      throw new Error('انتهت الجلسة، يرجى تسجيل الدخول مجددًا.')
    }

    const payload = await response.json().catch(() => null)
    if (!response.ok || !payload?.success) {
      throw new Error(payload?.message || 'تعذّر تحميل البيانات.')
    }
    return Array.isArray(payload.data) ? payload.data : []
  }

  function statusBadge(status) {
    const badge = el('span', `activity-status ${STATUS_LABELS[status] ? status : ''}`, STATUS_LABELS[status] || 'غير معروف')
    return badge
  }

  function createItem({ title, subtitle, summary, status, date, contact }) {
    const item = el('article', 'activity-item')
    const top = el('div', 'activity-item-top')
    const heading = el('div')
    heading.append(el('div', 'activity-person', title || 'بدون اسم'))
    if (subtitle) heading.append(el('span', 'activity-secondary', subtitle))
    top.append(heading, statusBadge(status))
    item.append(top)
    if (summary) item.append(el('p', 'activity-summary', shortText(summary)))
    const meta = el('div', 'activity-meta')
    if (contact) meta.append(el('span', '', contact))
    meta.append(el('time', '', dateText(date)))
    item.append(meta)
    return item
  }

  function renderEmpty(container, message) {
    container.replaceChildren(el('p', 'activity-state', message))
    container.setAttribute('aria-busy', 'false')
  }

  function renderError(container, path, message) {
    const state = el('p', 'activity-state error')
    state.append(document.createTextNode(message || 'تعذّر تحميل البيانات. '))
    const retry = el('button', 'activity-retry', 'إعادة المحاولة')
    retry.type = 'button'
    retry.addEventListener('click', () => loadPanel(container, path))
    state.append(retry)
    container.replaceChildren(state)
    container.setAttribute('aria-busy', 'false')
  }

  async function loadPanel(container, path) {
    container.setAttribute('aria-busy', 'true')
    container.replaceChildren(el('p', 'activity-state', 'جارٍ التحميل…'))
    try {
      const rows = await getRows(path)
      if (!rows.length) {
        renderEmpty(container, 'لا توجد سجلات حتى الآن.')
        return
      }
      const fragment = document.createDocumentFragment()
      rows.forEach((row) => {
        if (path === 'inquiries') {
          const subtitle = row.product?.name ? `المنتج: ${row.product.name}` : (row.source === 'whatsapp' ? 'استفسار عبر واتساب' : 'استفسار عام')
          const contact = row.phone || row.email || ''
          fragment.append(createItem({
            title: row.name,
            subtitle,
            summary: row.message,
            status: row.status,
            date: row.createdAt,
            contact,
          }))
        } else {
          const subtitle = [row.businessName, row.city, row.area].filter(Boolean).join(' · ')
          const contact = row.phone || row.whatsapp || row.email || ''
          const type = BUSINESS_TYPES[row.businessType] || row.businessType || ''
          fragment.append(createItem({
            title: row.name,
            subtitle: [subtitle, type].filter(Boolean).join(' · '),
            summary: row.message,
            status: row.status,
            date: row.createdAt,
            contact,
          }))
        }
      })
      container.replaceChildren(fragment)
      container.setAttribute('aria-busy', 'false')
    } catch (error) {
      renderError(container, path, error?.message || 'تعذّر تحميل البيانات.')
    }
  }

  const inquiries = $('recentInquiries')
  const distribution = $('recentDistribution')
  if (inquiries) loadPanel(inquiries, 'inquiries')
  if (distribution) loadPanel(distribution, 'distribution-requests')
})()
