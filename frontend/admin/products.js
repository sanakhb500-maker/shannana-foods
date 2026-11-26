(() => {
  const { $, h, api, ApiError, toast, initShell } = window.Admin
  const TONES = { red: 'أحمر', gold: 'ذهبي', rose: 'وردي', green: 'أخضر' }
  const MAX_BYTES = 8 * 1024 * 1024, MIN_SIDE = 300, MAX_SIDE = 8000
  const nf = new Intl.NumberFormat('ar-EG-u-nu-latn')
  const SPEC_FIELDS = [
    ['netContent', 'f_netContent'], ['netContentUnit', 'f_netContentUnit'],
    ['packType', 'f_packType'], ['packTypeEn', 'f_packTypeEn'],
    ['packMaterial', 'f_packMaterial'], ['packMaterialEn', 'f_packMaterialEn'], ['barcode', 'f_barcode'],
    ['cartonUnits', 'f_cartonUnits'], ['cartonLengthCm', 'f_cartonLengthCm'],
    ['cartonWidthCm', 'f_cartonWidthCm'], ['cartonHeightCm', 'f_cartonHeightCm'],
    ['cartonNetWeightKg', 'f_cartonNetWeightKg'], ['cartonGrossWeightKg', 'f_cartonGrossWeightKg'],
    ['shelfLifeValue', 'f_shelfLifeValue'], ['shelfLifeUnit', 'f_shelfLifeUnit'],
    ['storageNote', 'f_storageNote'], ['storageNoteEn', 'f_storageNoteEn'],
    ['originCountry', 'f_originCountry'], ['hsCode', 'f_hsCode'],
    ['palletCartons', 'f_palletCartons'], ['palletCartonsPerLayer', 'f_palletCartonsPerLayer'],
    ['exportNote', 'f_exportNote'], ['exportNoteEn', 'f_exportNoteEn'],
  ]

  function fillSpecifications(product) {
    for (const [key, id] of SPEC_FIELDS) {
      const field = $(id)
      field.value = product?.[key] == null ? '' : String(product[key])
    }
    form.querySelectorAll('[data-spec-section]').forEach((section) => {
      const hasExistingValue = [...section.querySelectorAll('[data-spec-field]')]
        .some((field) => String(field.value ?? '').trim() !== '')
      section.open = Boolean(product && hasExistingValue)
    })
  }

  let categories = []
  let editing = null // المنتج الجاري تعديله (أو null عند الإنشاء)
  let chosenFile = null
  let searchTimer = 0, loadSeq = 0

  const dlg = $('dlg'), form = $('pForm')

  // ---------- القائمة ----------
  function query() {
    const p = new URLSearchParams()
    const q = $('search').value.trim()
    if (q) p.set('search', q)
    if ($('fCategory').value) p.set('category', $('fCategory').value)
    p.set('status', $('fStatus').value)
    p.set('limit', '200')
    return p.toString()
  }

  async function load() {
    const seq = ++loadSeq
    $('pageError').hidden = true
    try {
      const res = await api('/admin/products?' + query())
      if (seq !== loadSeq) return // طلب أحدث بدأ
      render(res.data)
    } catch (e) {
      if (e.message === 'unauthorized') return
      $('loading').hidden = true
      $('pageError').textContent = 'تعذّر تحميل المنتجات: ' + e.message
      $('pageError').hidden = false
    }
  }

  function render(list) {
    $('loading').hidden = true
    $('empty').hidden = list.length > 0
    $('tbl').hidden = list.length === 0
    $('count').textContent = list.length ? `${nf.format(list.length)} منتج` : ''
    const rows = $('rows')
    rows.replaceChildren(...list.map(row))
  }

  function statusBadges(p) {
    if (!p.isActive) return h('span', { class: 'badge off', text: 'معطّل' })
    if (!p.category.isActive) return h('span', { class: 'badge warn', text: 'مخفي (التصنيف معطّل)' })
    return h('span', { class: 'badge on', text: 'نشط' })
  }

  function row(p) {
    const tr = h('tr', { class: p.isActive ? '' : 'is-off' },
      h('td', { 'data-label': 'الصورة' }, h('div', { class: 'thumb-box' }, h('img', { class: 'thumb', src: p.imageUrl, alt: '', loading: 'lazy' }))),
      h('td', { 'data-label': 'المنتج', class: 'cell-main' },
        h('strong', { text: p.name }), p.isFeatured ? h('span', { class: 'star', title: 'منتج مميّز', text: ' ★' }) : null,
        p.nameEn ? h('small', { class: 'slug', dir: 'ltr', lang: 'en', text: p.nameEn }) : null,
        h('small', { class: 'slug', dir: 'ltr', text: p.slug })),
      h('td', { 'data-label': 'التصنيف', text: p.category.name }),
      h('td', { 'data-label': 'اللون' }, h('span', { class: 'tone-dot tone-' + p.tone }), ' ' + (TONES[p.tone] || p.tone)),
      h('td', { 'data-label': 'الترتيب', text: nf.format(p.sortOrder) }),
      h('td', { 'data-label': 'الحالة' }, statusBadges(p)),
      h('td', { 'data-label': 'إجراءات', class: 'actions' }, h('div', { class: 'actions-in' },
        h('button', { class: 'btn btn-ghost sm', type: 'button', text: 'تعديل', onclick: (e) => { e.stopPropagation(); openDialog(p) } }),
        h('a', {
          class: 'btn btn-ghost sm',
          href: `/api/admin/products/${encodeURIComponent(p.id)}/technical-sheet.pdf`,
          download: '',
          title: 'تحميل ورقة المواصفات الفنية',
          'aria-label': `تحميل ورقة المواصفات الفنية للمنتج: ${p.name}`,
          text: 'تحميل PDF',
          onclick: (e) => e.stopPropagation(),
        }),
        h('button', { class: 'btn btn-ghost sm', type: 'button', text: p.isActive ? 'تعطيل' : 'تفعيل', onclick: (e) => { e.stopPropagation(); toggle(p) } }))))

    // The row opens the same edit dialog as the existing Edit button.
    // Interactive controls keep their own behavior and never trigger the row action.
    tr.dataset.editable = 'true'
    tr.tabIndex = 0
    tr.setAttribute('aria-label', `فتح تعديل المنتج: ${p.name}`)
    tr.setAttribute('aria-haspopup', 'dialog')
    tr.addEventListener('click', (event) => {
      if (event.target.closest('button, a, input, select, textarea, label')) return
      openDialog(p)
    })
    tr.addEventListener('keydown', (event) => {
      if (event.target !== tr || (event.key !== 'Enter' && event.key !== ' ')) return
      event.preventDefault()
      openDialog(p)
    })
    return tr
  }

  async function toggle(p) {
    const next = !p.isActive
    if (!next && !confirm(`تعطيل «${p.name}»؟ سيختفي من الموقع العام ويمكنك تفعيله لاحقًا.`)) return
    try {
      await api('/admin/products/' + p.id, { method: 'PATCH', json: { isActive: next } })
      toast(next ? 'تم تفعيل المنتج' : 'تم تعطيل المنتج')
      load()
    } catch (e) { if (e.message !== 'unauthorized') toast(e.message, 'err') }
  }

  // ---------- نافذة الإضافة/التعديل ----------
  function setImages({ current, fresh, caption }) {
    $('curFig').hidden = !current
    if (current) $('curImg').src = current
    $('newFig').hidden = !fresh
    if (fresh) $('newImg').src = fresh
    if (caption) $('newCap').textContent = caption
    $('noImg').hidden = !!(current || fresh)
  }

  function clearErrors() {
    $('formError').hidden = true
    form.querySelectorAll('.field.invalid').forEach((f) => f.classList.remove('invalid'))
    form.querySelectorAll('.ferr').forEach((e) => { e.textContent = '' })
  }
  function fieldError(name, msg) {
    const f = form.querySelector(`.field[data-field="${name}"]`)
    if (!f) return false
    f.classList.add('invalid'); f.querySelector('.ferr').textContent = msg
    return true
  }
  function showErrors(e) {
    const general = []
    if (e.errors?.length) e.errors.forEach((x) => { if (!fieldError(x.field, x.message)) general.push(x.message) })
    else general.push(e.message)
    if (general.length) { $('formError').textContent = general.join(' — '); $('formError').hidden = false }
    form.querySelector('.field.invalid input, .field.invalid select, .field.invalid textarea')?.focus()
  }

  function openDialog(p) {
    editing = p || null
    chosenFile = null
    clearErrors()
    form.reset()
    $('dlgTitle').textContent = p ? 'تعديل المنتج' : 'منتج جديد'
    const sel = $('f_cat')
    sel.replaceChildren(...categories.map((c) => h('option', { value: c.id, text: c.name + (c.nameEn ? ' / ' + c.nameEn : '') + (c.isActive ? '' : ' (معطّل)') })))
    $('f_name').value = p?.name ?? ''
    $('f_nameEn').value = p?.nameEn ?? ''
    $('f_slug').value = p?.slug ?? ''
    $('f_slug').readOnly = !!p
    $('slugHint').hidden = !!p
    $('f_cat').value = p?.categoryId ?? (categories.find((c) => c.isActive)?.id ?? '')
    $('f_tone').value = p?.tone ?? 'gold'
    $('f_order').value = p ? p.sortOrder : ''
    $('f_desc').value = p?.description ?? ''
    $('f_descEn').value = p?.descriptionEn ?? ''
    $('f_active').checked = p ? p.isActive : true
    $('f_feat').checked = p ? p.isFeatured : false
    fillSpecifications(p)
    setImages({ current: p?.imageUrl, fresh: null })
    $('saveBtn').disabled = false; $('saveBtn').textContent = 'حفظ'
    dlg.showModal()
    $('f_name').focus()
  }
  const closeDialog = () => dlg.close()

  // فحص مبدئي في المتصفح لتجربة أسرع (الخادم هو المرجع النهائي)
  function checkFile(file) {
    return new Promise((resolve) => {
      if (file.size > MAX_BYTES) return resolve('حجم الصورة أكبر من 8 ميغابايت.')
      if (!/^image\/(png|jpeg|webp)$/.test(file.type) && !/\.(png|jpe?g|webp)$/i.test(file.name)) return resolve('النوع غير مدعوم. المسموح: JPEG أو PNG أو WebP.')
      const reader = new FileReader()
      reader.onerror = () => resolve('تعذّر قراءة الملف.')
      reader.onload = () => {
        const img = new Image()
        img.onerror = () => resolve('الملف ليس صورة صالحة.')
        img.onload = () => {
          if (img.naturalWidth < MIN_SIDE || img.naturalHeight < MIN_SIDE) return resolve(`الصورة صغيرة (الحد الأدنى ${MIN_SIDE}×${MIN_SIDE} بكسل).`)
          if (img.naturalWidth > MAX_SIDE || img.naturalHeight > MAX_SIDE) return resolve(`الصورة كبيرة (الحد الأقصى ${MAX_SIDE}×${MAX_SIDE} بكسل).`)
          resolve({ url: reader.result, w: img.naturalWidth, h: img.naturalHeight })
        }
        img.src = reader.result // data: URL (متوافق مع CSP)
      }
      reader.readAsDataURL(file)
    })
  }

  $('f_image').addEventListener('change', async (e) => {
    const file = e.target.files[0]
    const f = form.querySelector('.field[data-field="image"]')
    f.classList.remove('invalid'); f.querySelector('.ferr').textContent = ''
    chosenFile = null
    if (!file) return setImages({ current: editing?.imageUrl, fresh: null })
    const r = await checkFile(file)
    if (typeof r === 'string') {
      e.target.value = ''
      fieldError('image', r)
      return setImages({ current: editing?.imageUrl, fresh: null })
    }
    chosenFile = file
    setImages({ current: editing?.imageUrl, fresh: r.url, caption: `الصورة الجديدة (${r.w}×${r.h}) — لن تُحفظ إلا بعد الضغط على «حفظ»` })
  })

  form.addEventListener('submit', async (e) => {
    e.preventDefault()
    clearErrors()
    if (!editing && !chosenFile) return fieldError('image', 'اختر صورة للمنتج.')
    const fd = new FormData()
    fd.set('name', $('f_name').value)
    fd.set('nameEn', $('f_nameEn').value)
    fd.set('categoryId', $('f_cat').value)
    fd.set('tone', $('f_tone').value)
    fd.set('description', $('f_desc').value)
    fd.set('descriptionEn', $('f_descEn').value)
    for (const [key, id] of SPEC_FIELDS) fd.set(key, $(id).value.trim())
    if ($('f_order').value !== '') fd.set('sortOrder', $('f_order').value)
    fd.set('isActive', String($('f_active').checked))
    fd.set('isFeatured', String($('f_feat').checked))
    if (!editing && $('f_slug').value.trim()) fd.set('slug', $('f_slug').value.trim())
    if (chosenFile) fd.set('image', chosenFile)

    const btn = $('saveBtn')
    btn.disabled = true; btn.textContent = chosenFile ? 'جارٍ رفع الصورة وحفظ المنتج…' : 'جارٍ الحفظ…'
    try {
      await api(editing ? '/admin/products/' + editing.id : '/admin/products', { method: editing ? 'PATCH' : 'POST', form: fd })
      toast(editing ? 'تم حفظ التعديلات' : 'تمت إضافة المنتج')
      closeDialog()
      load()
    } catch (err) {
      if (err.message === 'unauthorized') return
      showErrors(err instanceof ApiError ? err : new ApiError(0, 'حدث خطأ غير متوقع'))
      btn.disabled = false; btn.textContent = 'حفظ'
    }
  })

  $('dlgClose').addEventListener('click', closeDialog)
  $('cancelBtn').addEventListener('click', closeDialog)
  $('newBtn').addEventListener('click', () => openDialog(null))

  // ---------- المرشحات ----------
  $('search').addEventListener('input', () => { clearTimeout(searchTimer); searchTimer = setTimeout(load, 300) })
  $('fCategory').addEventListener('change', load)
  $('fStatus').addEventListener('change', load)

  // ---------- بدء ----------
  ;(async () => {
    try {
      await initShell()
      categories = (await api('/admin/categories')).data
      $('fCategory').append(...categories.map((c) => h('option', { value: c.id, text: c.name + (c.nameEn ? ' / ' + c.nameEn : '') + (c.isActive ? '' : ' (معطّل)') })))
      await load()
    } catch (e) {
      if (e.message !== 'unauthorized') { $('loading').hidden = true; $('pageError').textContent = 'تعذّر تحميل الصفحة: ' + e.message; $('pageError').hidden = false }
    }
  })()
})()
