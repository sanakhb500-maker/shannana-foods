(() => {
  const { $, h, api, toast, initShell } = window.Admin

  const STATUS_LABELS = {
    new: 'جديد',
    under_review: 'قيد المراجعة',
    awaiting_customer_approval: 'بانتظار موافقة العميل',
    confirmed: 'مؤكد',
    rejected: 'مرفوض',
  }
  const NEXT_STATUSES = {
    new: ['under_review', 'rejected'],
    under_review: ['awaiting_customer_approval', 'rejected'],
    awaiting_customer_approval: ['confirmed', 'rejected'],
    confirmed: [],
    rejected: [],
  }
  const numberFormat = new Intl.NumberFormat('ar-SY')
  const dateFormat = new Intl.DateTimeFormat('ar-SY', { dateStyle: 'medium', timeStyle: 'short' })

  let page = 1
  let pageSize = 20
  let meta = { page: 1, pageSize, total: 0, totalPages: 1 }
  let activeOrder = null
  let searchTimer = 0
  let requestSequence = 0

  const referenceOf = (id) => `SF-${String(id).padStart(6, '0')}`
  const formatDate = (value) => {
    const date = new Date(value)
    return Number.isNaN(date.getTime()) ? '—' : dateFormat.format(date)
  }
  const formatNumber = (value) => numberFormat.format(Number(value || 0))
  const valueOrDash = (value) => (value == null || String(value).trim() === '' ? '—' : String(value))

  function statusBadge(status) {
    return h('span', { class: `badge po-status status-${status}`, text: STATUS_LABELS[status] || status })
  }

  function orderRow(order) {
    const itemNames = (order.items || []).slice(0, 3).map((item) => item.productName)
    const extraItems = Math.max(0, (order.items || []).length - itemNames.length)
    const itemsText = itemNames.join('، ') + (extraItems ? `، و${formatNumber(extraItems)} أخرى` : '')
    const totalUnits = (order.items || []).reduce((sum, item) => sum + Number(item.totalUnits || 0), 0)

    return h('tr', null,
      h('td', { 'data-label': 'رقم الطلب' },
        h('strong', { class: 'po-primary', text: referenceOf(order.id) }),
        h('small', { class: 'po-secondary', text: `#${order.id}` }),
      ),
      h('td', { 'data-label': 'العميل / المحل' },
        h('strong', { class: 'po-primary', text: order.customerName }),
        h('small', { class: 'po-secondary', text: order.businessName }),
      ),
      h('td', { 'data-label': 'التواصل' },
        h('span', { class: 'po-primary', dir: 'ltr', text: order.phone }),
        h('small', { class: 'po-secondary', text: order.city }),
      ),
      h('td', { 'data-label': 'المنتجات والكميات' },
        h('span', { class: 'po-items-summary', text: `${formatNumber((order.items || []).length)} منتج — ${formatNumber(totalUnits)} وحدة` }),
        h('small', { class: 'po-secondary', text: itemsText || '—' }),
      ),
      h('td', { 'data-label': 'تاريخ الطلب', text: formatDate(order.createdAt) }),
      h('td', { 'data-label': 'الحالة' }, statusBadge(order.status)),
      h('td', { 'data-label': 'التفاصيل' },
        h('button', {
          class: 'btn btn-ghost po-open-btn',
          type: 'button',
          text: 'عرض التفاصيل',
          onclick: () => openOrder(order.id),
        }),
      ),
    )
  }

  function updatePagination() {
    const pagination = $('pagination')
    pagination.hidden = meta.totalPages <= 1
    $('pageLabel').textContent = `الصفحة ${formatNumber(meta.page)} من ${formatNumber(meta.totalPages)}`
    $('prevPage').disabled = meta.page <= 1
    $('nextPage').disabled = meta.page >= meta.totalPages
  }

  async function loadOrders() {
    const seq = ++requestSequence
    $('pageError').hidden = true
    $('loading').hidden = false
    $('empty').hidden = true
    $('ordersTable').hidden = true

    const params = new URLSearchParams({
      status: $('statusFilter').value,
      page: String(page),
      pageSize: String(pageSize),
    })
    const search = $('search').value.trim()
    if (search) params.set('search', search)

    try {
      const response = await api('/admin/purchase-orders?' + params.toString())
      if (seq !== requestSequence) return
      const rows = response.data || []
      meta = response.meta || { page, pageSize, total: rows.length, totalPages: 1 }
      $('rows').replaceChildren(...rows.map(orderRow))
      $('loading').hidden = true
      $('empty').hidden = rows.length !== 0
      $('ordersTable').hidden = rows.length === 0
      $('count').textContent = `إجمالي النتائج: ${formatNumber(meta.total || 0)}`
      const counts = meta.counts || {}
      $('totalCount').textContent = formatNumber(counts.all ?? meta.total ?? 0)
      $('newCount').textContent = formatNumber(counts.new || 0)
      $('reviewCount').textContent = formatNumber(counts.under_review || 0)
      $('awaitingCount').textContent = formatNumber(counts.awaiting_customer_approval || 0)
      updatePagination()
    } catch (error) {
      if (error.message === 'unauthorized' || seq !== requestSequence) return
      $('loading').hidden = true
      $('pageError').textContent = 'تعذّر تحميل الطلبات: ' + error.message
      $('pageError').hidden = false
      $('count').textContent = ''
    }
  }

  function fillText(id, value) {
    $(id).textContent = valueOrDash(value)
  }

  function buildStatusOptions(order) {
    const select = $('nextStatus')
    const next = NEXT_STATUSES[order.status] || []
    const choices = [h('option', { value: '', text: next.length ? 'اختاري الحالة التالية' : 'لا توجد مرحلة تالية' })]
    for (const status of next) choices.push(h('option', { value: status, text: STATUS_LABELS[status] }))
    select.replaceChildren(...choices)
    select.disabled = next.length === 0
    $('saveStatusBtn').disabled = next.length === 0
    $('statusHint').textContent = next.length
      ? 'يمكن نقل الطلب إلى المرحلة التالية فقط. لا يتم التأكيد إلا بعد مراجعة التوفر وموافقة العميل.'
      : 'هذه حالة نهائية؛ لا يمكن إعادة فتح الطلب من هذه الشاشة.'
  }

  function renderOrderDetails(order) {
    activeOrder = order
    $('dialogTitle').textContent = 'تفاصيل طلب الشراء'
    $('dialogReference').textContent = referenceOf(order.id)
    $('detailStatus').replaceChildren(statusBadge(order.status))
    fillText('detailCustomer', order.customerName)
    fillText('detailBusiness', order.businessName)
    fillText('detailPhone', order.phone)
    fillText('detailWhatsapp', order.whatsapp)
    fillText('detailCity', order.city)
    fillText('detailArea', order.area)
    fillText('detailEmail', order.email)
    fillText('detailCreated', formatDate(order.createdAt))
    fillText('detailAddress', order.address)
    $('detailCustomerNote').textContent = valueOrDash(order.customerNote) === '—' ? 'لا توجد ملاحظات.' : order.customerNote
    $('adminNote').value = order.adminNote || ''

    const items = order.items || []
    $('itemRows').replaceChildren(...items.map((item) => h('tr', null,
      h('td', { 'data-label': 'المنتج', text: item.productName }),
      h('td', { 'data-label': 'الكراتين', text: formatNumber(item.cartonQuantity) }),
      h('td', { 'data-label': 'الوحدات المفردة', text: formatNumber(item.unitQuantity) }),
      h('td', { 'data-label': 'عدد الوحدات بالكرتونة', text: item.unitsPerCarton == null ? '—' : formatNumber(item.unitsPerCarton) }),
      h('td', { 'data-label': 'الإجمالي بالوحدات', text: formatNumber(item.totalUnits) }),
    )))
    const totalUnits = items.reduce((sum, item) => sum + Number(item.totalUnits || 0), 0)
    $('detailTotalUnits').textContent = `${formatNumber(totalUnits)} وحدة`
    buildStatusOptions(order)
    $('detailError').hidden = true
  }

  async function openOrder(id) {
    $('detailError').hidden = true
    $('dialogTitle').textContent = 'جارٍ تحميل تفاصيل الطلب…'
    $('dialogReference').textContent = referenceOf(id)
    if (!$('orderDialog').open) $('orderDialog').showModal()
    try {
      const response = await api(`/admin/purchase-orders/${id}`)
      renderOrderDetails(response.data)
    } catch (error) {
      if (error.message === 'unauthorized') return
      $('detailError').textContent = 'تعذّر تحميل تفاصيل الطلب: ' + error.message
      $('detailError').hidden = false
      $('dialogTitle').textContent = 'تعذّر تحميل الطلب'
    }
  }

  async function saveStatus() {
    if (!activeOrder) return
    const status = $('nextStatus').value
    if (!status) {
      toast('اختاري الحالة التالية أولًا', 'err')
      return
    }
    const button = $('saveStatusBtn')
    button.disabled = true
    try {
      const response = await api(`/admin/purchase-orders/${activeOrder.id}`, { method: 'PATCH', json: { status } })
      renderOrderDetails(response.data)
      toast('تم تحديث حالة الطلب بنجاح')
      await loadOrders()
    } catch (error) {
      if (error.message !== 'unauthorized') {
        $('detailError').textContent = error.message
        $('detailError').hidden = false
        toast(error.message, 'err')
      }
    } finally {
      button.disabled = false
      if (activeOrder) buildStatusOptions(activeOrder)
    }
  }

  async function saveAdminNote() {
    if (!activeOrder) return
    const button = $('saveNoteBtn')
    button.disabled = true
    try {
      const response = await api(`/admin/purchase-orders/${activeOrder.id}`, {
        method: 'PATCH',
        json: { adminNote: $('adminNote').value },
      })
      renderOrderDetails(response.data)
      toast('تم حفظ الملاحظة الداخلية')
    } catch (error) {
      if (error.message !== 'unauthorized') {
        $('detailError').textContent = error.message
        $('detailError').hidden = false
        toast(error.message, 'err')
      }
    } finally {
      button.disabled = false
    }
  }

  function bindEvents() {
    $('refreshBtn').addEventListener('click', loadOrders)
    $('statusFilter').addEventListener('change', () => { page = 1; loadOrders() })
    $('search').addEventListener('input', () => {
      window.clearTimeout(searchTimer)
      searchTimer = window.setTimeout(() => { page = 1; loadOrders() }, 300)
    })
    $('prevPage').addEventListener('click', () => { if (page > 1) { page--; loadOrders() } })
    $('nextPage').addEventListener('click', () => { if (page < meta.totalPages) { page++; loadOrders() } })
    $('dialogClose').addEventListener('click', () => $('orderDialog').close())
    $('saveStatusBtn').addEventListener('click', saveStatus)
    $('saveNoteBtn').addEventListener('click', saveAdminNote)
    $('orderDialog').addEventListener('click', (event) => {
      if (event.target === $('orderDialog')) $('orderDialog').close()
    })
  }

  async function start() {
    try {
      await initShell()
      bindEvents()
      await loadOrders()
    } catch (error) {
      if (error.message !== 'unauthorized') toast(error.message, 'err')
    }
  }

  start()
})()
