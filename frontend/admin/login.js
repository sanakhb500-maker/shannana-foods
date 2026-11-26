(() => {
  const $ = (id) => document.getElementById(id)
  const form = $('loginForm'), emailEl = $('email'), pwEl = $('password'), errEl = $('formError'), btn = $('submitBtn')

  // شعار احتياطي إن لم يوجد الملف (بدون معالج inline بسبب CSP)
  const logo = $('logo')
  const showFallback = () => { logo.hidden = true; $('logoFallback').hidden = false }
  logo.addEventListener('error', showFallback)
  if (logo.complete && logo.naturalWidth === 0) showFallback()

  $('pwToggle').addEventListener('click', (e) => {
    const show = pwEl.type === 'password'
    pwEl.type = show ? 'text' : 'password'
    e.currentTarget.textContent = show ? 'إخفاء' : 'إظهار'
    e.currentTarget.setAttribute('aria-label', show ? 'إخفاء كلمة المرور' : 'إظهار كلمة المرور')
  })

  const setError = (msg, fields = []) => {
    errEl.textContent = msg
    errEl.hidden = !msg
    $('emailField').classList.toggle('invalid', fields.includes('email'))
    $('passwordField').classList.toggle('invalid', fields.includes('password'))
  }

  form.addEventListener('submit', async (e) => {
    e.preventDefault()
    setError('')
    const email = emailEl.value.trim(), password = pwEl.value
    if (!email || !password) return setError('أدخل البريد الإلكتروني وكلمة المرور.', [!email && 'email', !password && 'password'].filter(Boolean))

    btn.disabled = true
    const label = btn.textContent
    btn.textContent = 'جارٍ التحقق…'
    try {
      const res = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({ email, password }),
      })
      const body = await res.json().catch(() => null)
      if (res.ok && body?.success) return location.replace('/admin')
      if (res.status === 400 && Array.isArray(body?.errors)) {
        return setError(body.errors[0].message, body.errors.map((x) => x.field))
      }
      if (res.status === 401) { pwEl.value = ''; pwEl.focus() } // لا نُبقي كلمة مرور خاطئة في الحقل
      setError(body?.message || 'تعذّر تسجيل الدخول، حاول مجددًا.', res.status === 401 ? ['email', 'password'] : [])
    } catch {
      setError('تعذّر الاتصال بالخادم. تحقق من الاتصال وحاول مجددًا.')
    } finally {
      btn.disabled = false
      btn.textContent = label
    }
  })
})()
