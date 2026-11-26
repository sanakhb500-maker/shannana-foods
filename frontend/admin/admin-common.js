// أدوات مشتركة لصفحات الإدارة: طلبات API (كوكي الجلسة تلقائيًا)، بناء DOM آمن، إشعارات، وهيكل الصفحة.
window.Admin = (() => {
  const $ = (id) => document.getElementById(id)

  class ApiError extends Error {
    constructor(status, message, errors) {
      super(message)
      this.status = status
      this.errors = errors || []
    }
  }

  // json: كائن يُرسل كـ JSON | form: FormData (لا نضبط Content-Type كي يضيف المتصفح boundary)
  async function api(path, { method = 'GET', json, form } = {}) {
    const headers = { Accept: 'application/json' }
    let body
    if (json !== undefined) { headers['Content-Type'] = 'application/json'; body = JSON.stringify(json) }
    else if (form) body = form
    let res
    try { res = await fetch('/api' + path, { method, headers, body }) }
    catch { throw new ApiError(0, 'تعذّر الاتصال بالخادم. تحقق من الاتصال وحاول مجددًا.') }
    const data = await res.json().catch(() => null)
    if (res.status === 401) { location.replace('/admin/login'); throw new ApiError(401, 'unauthorized') }
    if (!res.ok || !data?.success) throw new ApiError(res.status, data?.message || 'حدث خطأ غير متوقع', data?.errors)
    return data // { data, meta }
  }

  // بناء عناصر بـ textContent فقط (لا innerHTML) لأن البيانات قادمة من القاعدة
  function h(tag, props, ...kids) {
    const el = document.createElement(tag)
    for (const [k, v] of Object.entries(props || {})) {
      if (v == null || v === false) continue
      if (k === 'class') el.className = v
      else if (k === 'text') el.textContent = v
      else if (k === 'dataset') Object.assign(el.dataset, v)
      else if (k.startsWith('on') && typeof v === 'function') el.addEventListener(k.slice(2), v)
      else el.setAttribute(k, v === true ? '' : v)
    }
    for (const kid of kids.flat()) {
      if (kid == null || kid === false) continue
      el.append(kid.nodeType ? kid : document.createTextNode(String(kid)))
    }
    return el
  }

  function toast(message, type = 'ok') {
    let box = $('toasts')
    if (!box) { box = h('div', { id: 'toasts', class: 'toasts', role: 'status', 'aria-live': 'polite' }); document.body.append(box) }
    const t = h('div', { class: 'toast ' + type, text: message })
    box.append(t)
    setTimeout(() => t.classList.add('out'), type === 'err' ? 6000 : 3200)
    setTimeout(() => t.remove(), type === 'err' ? 6400 : 3600)
  }

  // هيكل الصفحة: المستخدم، تسجيل الخروج، قائمة الجوال، حماية زر الرجوع
  async function initShell() {
    const { data } = await api('/auth/me')
    const user = data.user
    $('userName').textContent = user.name
    $('userEmail').textContent = user.email
    $('avatar').textContent = [...user.name.trim()][0] || '·'

    // Keep Purchase Orders in one consistent position on every admin page:
    // after Categories and immediately before Inquiries.
    const sideNav = document.querySelector('.side-nav')
    if (sideNav) {
      let ordersLink = sideNav.querySelector('[data-purchase-orders-nav]')
        || sideNav.querySelector('a[href="/admin/purchase-orders"]')

      if (!ordersLink) {
        ordersLink = h('a', {
          class: 'nav-item',
          href: '/admin/purchase-orders',
          dataset: { purchaseOrdersNav: 'true' },
        }, h('span', { class: 'nav-symbol', 'aria-hidden': 'true' }, '🛒'), 'طلبات الشراء')
      }

      // Some pages (including the purchase-orders page itself) have a static link.
      // Reuse and move that node instead of creating a duplicate.
      ordersLink.dataset.purchaseOrdersNav = 'true'
      const inquiriesLink = sideNav.querySelector('a[href="/admin/inquiries"]')
      const categoriesLink = sideNav.querySelector('a[href="/admin/categories"]')
      if (inquiriesLink) sideNav.insertBefore(ordersLink, inquiriesLink)
      else if (categoriesLink) sideNav.insertBefore(ordersLink, categoriesLink.nextSibling)
      else sideNav.append(ordersLink)

      const isOrdersPage = location.pathname.replace(/\/$/, '') === '/admin/purchase-orders'
      ordersLink.classList.toggle('active', isOrdersPage)
      if (isOrdersPage) ordersLink.setAttribute('aria-current', 'page')
      else ordersLink.removeAttribute('aria-current')
    }

    $('logoutBtn').addEventListener('click', async () => {
      try { await fetch('/api/auth/logout', { method: 'POST', headers: { Accept: 'application/json' } }) } catch { /* نحوّل للدخول في كل الأحوال */ }
      location.replace('/admin/login')
    })
    const app = $('app'), menuBtn = $('menuBtn')
    const setNav = (open) => { app.classList.toggle('nav-open', open); menuBtn.setAttribute('aria-expanded', String(open)) }
    menuBtn.addEventListener('click', () => setNav(!app.classList.contains('nav-open')))
    $('scrim').addEventListener('click', () => setNav(false))
    document.addEventListener('keydown', (e) => { if (e.key === 'Escape') setNav(false) })
    window.addEventListener('pageshow', (e) => { if (e.persisted) location.reload() })
    return user
  }

  return { $, h, api, ApiError, toast, initShell }
})()
