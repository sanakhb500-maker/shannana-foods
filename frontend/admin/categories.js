(() => {
  const { $, h, api, ApiError, toast, initShell } = window.Admin
  const nf = new Intl.NumberFormat('ar-EG-u-nu-latn')
  let editing = null
  const dlg = $('dlg'), form = $('cForm')

  async function load() {
    $('pageError').hidden = true
    try {
      const { data } = await api('/admin/categories')
      $('loading').hidden = true
      $('empty').hidden = data.length > 0
      $('tbl').hidden = data.length === 0
      $('rows').replaceChildren(...data.map(row))
    } catch (e) {
      if (e.message === 'unauthorized') return
      $('loading').hidden = true
      $('pageError').textContent = 'تعذّر تحميل التصنيفات: ' + e.message
      $('pageError').hidden = false
    }
  }

  function row(c) {
    const del = h('button', {
      class: 'btn btn-ghost sm danger', type: 'button', text: 'حذف',
      disabled: c.productsCount > 0,
      title: c.productsCount > 0 ? 'لا يمكن حذف تصنيف يحتوي منتجات (عطّله بدل ذلك)' : 'حذف التصنيف نهائيًا',
      onclick: () => remove(c),
    })
    return h('tr', { class: c.isActive ? '' : 'is-off' },
      h('td', { 'data-label': 'التصنيف', class: 'cell-main' }, h('strong', { text: c.name }), c.nameEn ? h('small', { class: 'slug', dir: 'ltr', lang: 'en', text: c.nameEn }) : null, c.description ? h('small', { class: 'slug', text: c.description }) : null),
      h('td', { 'data-label': 'المعرّف' }, h('code', { dir: 'ltr', text: c.slug })),
      h('td', { 'data-label': 'الترتيب', text: nf.format(c.sortOrder) }),
      h('td', { 'data-label': 'المنتجات', text: `${nf.format(c.activeProductsCount)} نشط من ${nf.format(c.productsCount)}` }),
      h('td', { 'data-label': 'الحالة' }, h('span', { class: 'badge ' + (c.isActive ? 'on' : 'off'), text: c.isActive ? 'نشط' : 'معطّل' })),
      h('td', { 'data-label': 'إجراءات', class: 'actions' }, h('div', { class: 'actions-in' },
        h('button', { class: 'btn btn-ghost sm', type: 'button', text: 'تعديل', onclick: () => openDialog(c) }),
        h('button', { class: 'btn btn-ghost sm', type: 'button', text: c.isActive ? 'تعطيل' : 'تفعيل', onclick: () => toggle(c) }),
        del)))
  }

  async function toggle(c) {
    const next = !c.isActive
    if (!next && !confirm(`تعطيل التصنيف «${c.name}»؟ سيختفي التصنيف ومنتجاته النشطة (${c.activeProductsCount}) من الموقع العام.`)) return
    try {
      await api('/admin/categories/' + c.id, { method: 'PATCH', json: { isActive: next } })
      toast(next ? 'تم تفعيل التصنيف' : 'تم تعطيل التصنيف')
      load()
    } catch (e) { if (e.message !== 'unauthorized') toast(e.message, 'err') }
  }

  async function remove(c) {
    if (!confirm(`حذف التصنيف «${c.name}» نهائيًا؟ لا يمكن التراجع.`)) return
    try {
      await api('/admin/categories/' + c.id, { method: 'DELETE' })
      toast('تم حذف التصنيف')
      load()
    } catch (e) { if (e.message !== 'unauthorized') toast(e.message, 'err') }
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

  function openDialog(c) {
    editing = c || null
    clearErrors()
    form.reset()
    $('dlgTitle').textContent = c ? 'تعديل التصنيف' : 'تصنيف جديد'
    $('f_name').value = c?.name ?? ''
    $('f_nameEn').value = c?.nameEn ?? ''
    $('f_slug').value = c?.slug ?? ''
    $('f_slug').readOnly = !!c
    $('slugHint').hidden = !!c
    $('f_desc').value = c?.description ?? ''
    $('f_descEn').value = c?.descriptionEn ?? ''
    $('f_order').value = c ? c.sortOrder : ''
    $('f_active').checked = c ? c.isActive : true
    $('saveBtn').disabled = false; $('saveBtn').textContent = 'حفظ'
    dlg.showModal()
    $('f_name').focus()
  }

  form.addEventListener('submit', async (e) => {
    e.preventDefault()
    clearErrors()
    const body = { name: $('f_name').value, nameEn: $('f_nameEn').value, description: $('f_desc').value, descriptionEn: $('f_descEn').value, isActive: $('f_active').checked }
    if ($('f_order').value !== '') body.sortOrder = Number($('f_order').value)
    if (!editing) body.slug = $('f_slug').value
    const btn = $('saveBtn')
    btn.disabled = true; btn.textContent = 'جارٍ الحفظ…'
    try {
      await api(editing ? '/admin/categories/' + editing.id : '/admin/categories', { method: editing ? 'PATCH' : 'POST', json: body })
      toast(editing ? 'تم حفظ التعديلات' : 'تمت إضافة التصنيف')
      dlg.close()
      load()
    } catch (err) {
      if (err.message === 'unauthorized') return
      const general = []
      if (err instanceof ApiError && err.errors.length) err.errors.forEach((x) => { if (!fieldError(x.field, x.message)) general.push(x.message) })
      else general.push(err.message)
      if (general.length) { $('formError').textContent = general.join(' — '); $('formError').hidden = false }
      btn.disabled = false; btn.textContent = 'حفظ'
    }
  })

  $('dlgClose').addEventListener('click', () => dlg.close())
  $('cancelBtn').addEventListener('click', () => dlg.close())
  $('newBtn').addEventListener('click', () => openDialog(null))

  ;(async () => {
    try { await initShell(); await load() }
    catch (e) { if (e.message !== 'unauthorized') { $('loading').hidden = true; $('pageError').textContent = 'تعذّر تحميل الصفحة: ' + e.message; $('pageError').hidden = false } }
  })()
})()
