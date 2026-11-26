(() => {
  const $ = (id) => document.getElementById(id)
  const nf = new Intl.NumberFormat('ar-EG-u-nu-latn')

  // الجلسة تُدار بكوكي HttpOnly؛ أي 401 يعني انتهت الجلسة → صفحة الدخول.
  async function api(path, options = {}) {
    const res = await fetch('/api' + path, { headers: { Accept: 'application/json' }, ...options })
    const body = await res.json().catch(() => null)
    if (res.status === 401) { location.replace('/admin/login'); throw new Error('unauthorized') }
    if (!res.ok || !body?.success) throw new Error(body?.message || 'حدث خطأ غير متوقع')
    return body.data
  }

  const showError = (msg) => { const el = $('pageError'); el.textContent = msg; el.hidden = !msg }

  function fillStat(id, total, meta) {
    const el = $('stat-' + id)
    el.classList.remove('loading')
    el.querySelector('.num').textContent = nf.format(total)
    const m = el.querySelector('.meta')
    m.textContent = ''
    meta.forEach(([text, strong], i) => {
      if (i) m.append(' · ')
      if (strong) { const b = document.createElement('b'); b.textContent = text; m.append(b) } else m.append(text)
    })
  }

  async function init() {
    const { user } = await api('/auth/me')
    $('userName').textContent = user.name
    $('userEmail').textContent = user.email
    $('avatar').textContent = [...user.name.trim()][0] || '·'
    $('greeting').textContent = 'مرحبًا، ' + user.name
    try {
      const s = await api('/admin/stats')
      fillStat('products', s.products.total, [[nf.format(s.products.active) + ' نشط', true], ['من ' + nf.format(s.products.total)]])
      fillStat('categories', s.categories.total, [[nf.format(s.categories.active) + ' نشط', true], ['من ' + nf.format(s.categories.total)]])
      fillStat('inquiries', s.inquiries.total, [[nf.format(s.inquiries.new) + ' جديد', true]])
      fillStat('distributionRequests', s.distributionRequests.total, [[nf.format(s.distributionRequests.new) + ' جديد', true]])
    } catch (e) {
      if (e.message !== 'unauthorized') showError('تعذّر تحميل الإحصاءات: ' + e.message)
    }
  }

  $('logoutBtn').addEventListener('click', async () => {
    try { await fetch('/api/auth/logout', { method: 'POST', headers: { Accept: 'application/json' } }) } catch { /* نحوّل للدخول في كل الأحوال */ }
    location.replace('/admin/login')
  })

  // قائمة الجوال
  const app = $('app'), menuBtn = $('menuBtn')
  const setNav = (open) => { app.classList.toggle('nav-open', open); menuBtn.setAttribute('aria-expanded', String(open)) }
  menuBtn.addEventListener('click', () => setNav(!app.classList.contains('nav-open')))
  $('scrim').addEventListener('click', () => setNav(false))
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape') setNav(false) })

  // زر الرجوع بعد الخروج لا يُظهر لوحة مخزّنة
  window.addEventListener('pageshow', (e) => { if (e.persisted) location.reload() })

  init().catch((e) => { if (e.message !== 'unauthorized') showError('تعذّر تحميل اللوحة: ' + e.message) })
})()
