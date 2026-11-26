// نماذج الموقع العامة: الاستفسار وطلب التوزيع. الإرسال إلى /api/inquiries و/api/distribution-requests.
// بلا مكتبات؛ نصوص الرد تُعرض بـ textContent فقط.
(() => {
  const ENDPOINT = { inquiry: '/api/inquiries', distribution: '/api/distribution-requests' }
  const state = { token: '', submitting: new WeakSet() }
  // لغة الصفحة ونصوصها من الخادم (كتلة JSON #sf-i18n في القالب)
  const LANG = document.documentElement.lang === 'en' ? 'en' : 'ar'
  const I18N = (() => { try { return JSON.parse(document.getElementById('sf-i18n').textContent) } catch { return {} } })()
  const t = (key) => I18N[key] ?? key

  const $ = (root, sel) => root.querySelector(sel)

  async function fetchToken() {
    try {
      const res = await fetch('/api/forms/token', { headers: { Accept: 'application/json' }, cache: 'no-store' })
      const body = await res.json()
      state.token = body?.data?.token || ''
    } catch { state.token = '' }
  }

  function setError(form, name, msg) {
    const f = $(form, `.sf-field[data-field="${name}"]`)
    if (!f) return false
    f.classList.add('invalid')
    $(f, '.sf-err').textContent = msg
    return true
  }
  function clearErrors(form) {
    form.querySelectorAll('.sf-field.invalid').forEach((f) => f.classList.remove('invalid'))
    form.querySelectorAll('.sf-err').forEach((e) => { e.textContent = '' })
    const a = $(form, '.sf-alert'); a.hidden = true; a.textContent = ''
  }
  function showAlert(form, msg) {
    const a = $(form, '.sf-alert'); a.textContent = msg; a.hidden = false
  }

  // فحص سريع لتجربة أفضل؛ الخادم هو المرجع النهائي
  const PHONE = /^\+?\d{7,15}$/
  const digits = (v) => v.replace(/[\u0660-\u0669]/g, (d) => String(d.charCodeAt(0) - 0x660)).replace(/[\u06F0-\u06F9]/g, (d) => String(d.charCodeAt(0) - 0x6f0)).replace(/[\s\-.()]/g, '')
  function validate(kind, data) {
    const errs = {}
    if (data.name.trim().length < 2) errs.name = t('form.err.name')
    if (kind === 'inquiry') {
      if (!data.phone.trim() && !data.email.trim()) errs.phone = t('form.err.contact')
      if (data.message.trim().length < 5) errs.message = t('form.err.message')
    } else {
      if (data.businessName.trim().length < 2) errs.businessName = t('form.err.business')
      if (!data.businessType) errs.businessType = t('form.err.type')
      if (!data.phone.trim()) errs.phone = t('form.err.phoneRequired')
    }
    if (data.phone.trim() && !PHONE.test(digits(data.phone))) errs.phone = t('form.err.phone')
    if (data.whatsapp?.trim() && !PHONE.test(digits(data.whatsapp))) errs.whatsapp = t('form.err.whatsapp')
    return errs
  }

  function collect(form) {
    const data = {}
    new FormData(form).forEach((v, k) => { data[k] = typeof v === 'string' ? v : '' })
    return data
  }

  async function open(kind) {
    const dlg = document.getElementById(kind === 'inquiry' ? 'inquiryDlg' : 'distributionDlg')
    if (!dlg) return
    const form = $(dlg, 'form.sf-form')
    if (state.submitting.has(form)) return
    form.reset(); clearErrors(form)
    state.submitting.delete(form)
    form.hidden = false; $(dlg, '.sf-done').hidden = true
    const btn = $(form, '[type=submit]'); btn.disabled = false; btn.textContent = btn.dataset.label
    state.token = ''
    fetchToken() // رمز موقَّع من الخادم يُجلب عند فتح النموذج (يمنع الإرسال الفوري الآلي)
    dlg.showModal()
    $(form, 'input:not([type=hidden])').focus()
  }

  async function submit(form, kind) {
    // Guard before the first await so rapid clicks/Enter presses cannot send duplicates.
    if (state.submitting.has(form)) return

    clearErrors(form)
    const data = collect(form)
    const errs = validate(kind, data)
    if (Object.keys(errs).length) {
      Object.entries(errs).forEach(([k, m]) => setError(form, k, m))
      return $(form, '.sf-field.invalid input, .sf-field.invalid select, .sf-field.invalid textarea')?.focus()
    }

    const btn = $(form, '[type=submit]')
    const dlg = form.closest('dialog')
    const closeButtons = form.querySelectorAll('[data-close]')
    state.submitting.add(form)
    btn.disabled = true
    btn.textContent = t('form.sending')
    closeButtons.forEach((button) => { button.disabled = true })
    let succeeded = false

    try {
      if (!state.token) await fetchToken()
      const res = await fetch(ENDPOINT[kind], {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({ ...data, formToken: state.token, lang: LANG }),
      })
      const body = await res.json().catch(() => null)
      if (res.status === 201 && body?.success) {
        $(dlg, '.sf-done p').textContent = body.data?.message || t('form.okDefault')
        form.hidden = true
        $(dlg, '.sf-done').hidden = false
        $(dlg, '.sf-done .btn').focus()
        succeeded = true
        return
      }
      if (res.status === 400 && Array.isArray(body?.errors) && body.errors.length) {
        const general = []
        body.errors.forEach((e) => { if (!setError(form, e.field, e.message)) general.push(e.message) })
        if (general.length) showAlert(form, general.join(' — '))
        $(form, '.sf-field.invalid input, .sf-field.invalid select, .sf-field.invalid textarea')?.focus()
      } else {
        showAlert(form, body?.message || t('form.fail'))
        if (res.status === 400) state.token = '' // رمز منتهٍ/غير صالح: نجلب غيره عند المحاولة التالية
      }
    } catch {
      showAlert(form, t('form.network'))
    } finally {
      state.submitting.delete(form)
      closeButtons.forEach((button) => { button.disabled = false })
      if (!succeeded) {
        btn.disabled = false
        btn.textContent = btn.dataset.label
      }
    }
  }

  document.querySelectorAll('[data-open-form]').forEach((el) => el.addEventListener('click', (e) => { e.preventDefault(); open(el.dataset.openForm) }))
  document.querySelectorAll('dialog.sf-dlg').forEach((dlg) => {
    const form = $(dlg, 'form.sf-form')
    dlg.addEventListener('cancel', (event) => {
      if (state.submitting.has(form)) event.preventDefault()
    })
    dlg.querySelectorAll('[data-close]').forEach((b) => b.addEventListener('click', () => {
      if (!state.submitting.has(form)) dlg.close()
    }))
    $(form, '[type=submit]').dataset.label = $(form, '[type=submit]').textContent
    form.addEventListener('submit', (e) => { e.preventDefault(); submit(form, form.dataset.form) })
  })
})()
