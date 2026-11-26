/* Shannana Foods — guest purchase cart. Stores product selections only; customer data is never persisted. */
(() => {
  'use strict';

  const LANG = document.documentElement.lang === 'en' ? 'en' : 'ar';
  const I18N = (() => {
    try { return JSON.parse(document.getElementById('sf-i18n')?.textContent || '{}'); }
    catch { return {}; }
  })();
  // Fallback copy keeps the cart readable even when the server's client-side
  // translation payload does not yet contain the cart.* keys.
  const CART_COPY = {
    ar: {
      'cart.open': 'فتح سلة الطلبات',
      'cart.title': 'سلة الطلبات',
      'cart.subtitle': 'راجع المنتجات والكميات، ثم أرسل طلبك. سنتواصل معك لاحقًا لتأكيد السعر والتفاصيل.',
      'cart.addToCart': 'أضف إلى السلة',
      'cart.added': 'تمت إضافة المنتج إلى طلبك',
      'cart.empty': 'سلتك فارغة حاليًا',
      'cart.emptyHint': 'تصفّح المنتجات وأضف الكميات التي تحتاجها إلى طلبك.',
      'cart.keepBrowsing': 'متابعة التصفح',
      'cart.remove': 'حذف المنتج',
      'cart.cartons': 'عدد الكراتين',
      'cart.units': 'عدد الوحدات المفردة',
      'cart.cartonSize': 'كل كرتونة تحتوي {count} وحدة',
      'cart.noCartons': 'الطلب بالكراتين غير متاح لهذا المنتج',
      'cart.itemTotal': 'إجمالي وحدات المنتج',
      'cart.totalUnits': 'إجمالي الوحدات في الطلب',
      'cart.productCount': 'عدد المنتجات',
      'cart.checkout': 'المتابعة إلى بيانات العميل',
      'cart.back': 'العودة إلى السلة',
      'cart.detailsTitle': 'بيانات العميل',
      'cart.detailsLead': 'أدخل بيانات التواصل واسم المنشأة لإرسال طلب الشراء.',
      'cart.contactName': 'اسم الشخص المسؤول',
      'cart.businessName': 'اسم المحل أو المنشأة',
      'cart.phone': 'رقم الهاتف',
      'cart.city': 'المدينة',
      'cart.area': 'المنطقة',
      'cart.address': 'العنوان الكامل',
      'cart.email': 'البريد الإلكتروني',
      'cart.whatsapp': 'رقم واتساب إضافي',
      'cart.note': 'ملاحظات على الطلب',
      'cart.optional': 'اختياري',
      'cart.requiredNote': 'الحقول المعلّمة بنجمة إلزامية. لا تحتاج إلى إنشاء حساب لإرسال الطلب.',
      'cart.submit': 'إرسال طلب الشراء',
      'cart.sending': 'جارٍ إرسال الطلب...',
      'cart.genericError': 'تعذّر إرسال الطلب. احتفظنا بمحتويات السلة؛ يرجى المحاولة مجددًا.',
      'cart.successTitle': 'تم استلام طلبك بنجاح',
      'cart.successText': 'تم حفظ طلبك بنجاح. اضغط الزر لفتح واتساب ومشاركة تفاصيل الطلب مع فريق شنانة فودز. سيُراجع الفريق التوفر والسعر ويتواصل معك للتأكيد.',
      'cart.whatsappOpen': 'فتح واتساب وإرسال تفاصيل الطلب',
      'cart.whatsappHint': 'سيظهر نص جاهز في واتساب؛ اضغط إرسال من داخل واتساب لإرسال التفاصيل. حفظ الطلب في الموقع لا يعني تأكيده بعد.',
      'cart.whatsappUnavailable': 'تم حفظ الطلب، لكن تعذّر تجهيز رابط واتساب. احتفظ برقم الطلب وتواصل مع شنانة فودز.',
      'cart.reference': 'رقم الطلب',
      'cart.close': 'إغلاق',
      'cart.unitsWord': 'وحدة',
      'cart.securityWait': 'يرجى الانتظار لحظة حتى يكتمل التحقق قبل إرسال الطلب.',
      'cart.tokenError': 'تعذّر تجهيز نموذج الطلب. تحقّق من الاتصال ثم حاول مجددًا.',
      'cart.noPrices': 'الأسعار غير معروضة حاليًا؛ سيتم تأكيدها بعد مراجعة طلبك.',
      'cart.orderSummary': 'ملخص الطلب',
      'cart.cartonsShort': 'كرتونة',
      'cart.unitsShort': 'وحدة',
      'cart.tooManyProducts': 'يمكنك إضافة 50 منتجًا مختلفًا كحد أقصى إلى الطلب الواحد.',
      'cart.storageWarning': 'تعذّر حفظ السلة في هذا المتصفح؛ قد لا تبقى بعد إغلاق الصفحة.'
    },
    en: {
      'cart.open': 'Open order cart',
      'cart.title': 'Your order cart',
      'cart.subtitle': 'Review your products and quantities, then submit your request. We will contact you to confirm pricing and details.',
      'cart.addToCart': 'Add to cart',
      'cart.added': 'Product added to your order',
      'cart.empty': 'Your cart is empty',
      'cart.emptyHint': 'Browse the products and add the quantities you need.',
      'cart.keepBrowsing': 'Continue browsing',
      'cart.remove': 'Remove product',
      'cart.cartons': 'Number of cartons',
      'cart.units': 'Individual units',
      'cart.cartonSize': 'Each carton contains {count} units',
      'cart.noCartons': 'Carton ordering is unavailable for this product',
      'cart.itemTotal': 'Product unit total',
      'cart.totalUnits': 'Total units in this order',
      'cart.productCount': 'Number of products',
      'cart.checkout': 'Continue to customer details',
      'cart.back': 'Back to cart',
      'cart.detailsTitle': 'Customer details',
      'cart.detailsLead': 'Enter your contact and business information to submit the purchase order.',
      'cart.contactName': 'Contact person',
      'cart.businessName': 'Shop or business name',
      'cart.phone': 'Phone number',
      'cart.city': 'City',
      'cart.area': 'Area',
      'cart.address': 'Full address',
      'cart.email': 'Email address',
      'cart.whatsapp': 'Additional WhatsApp number',
      'cart.note': 'Order notes',
      'cart.optional': 'Optional',
      'cart.requiredNote': 'Fields marked with an asterisk are required. No account is needed.',
      'cart.submit': 'Submit purchase order',
      'cart.sending': 'Sending your order...',
      'cart.genericError': 'We could not submit your order. Your cart has been kept; please try again.',
      'cart.successTitle': 'Your order has been received',
      'cart.successText': 'Your order has been saved. Open WhatsApp to share the order details with Shannana Foods. The team will review availability and pricing and contact you for confirmation.',
      'cart.whatsappOpen': 'Open WhatsApp to send order details',
      'cart.whatsappHint': 'A prefilled message will open in WhatsApp. Press Send there to share it. Saving the order on the website does not mean it is confirmed.',
      'cart.whatsappUnavailable': 'Your order was saved, but a WhatsApp link could not be prepared. Keep the order reference and contact Shannana Foods.',
      'cart.reference': 'Order reference',
      'cart.close': 'Close',
      'cart.unitsWord': 'units',
      'cart.securityWait': 'Please wait a moment while the security check completes.',
      'cart.tokenError': 'Could not prepare the order form. Check your connection and try again.',
      'cart.noPrices': 'Prices are not shown yet; they will be confirmed after we review your order.',
      'cart.orderSummary': 'Order summary',
      'cart.cartonsShort': 'cartons',
      'cart.unitsShort': 'units',
      'cart.tooManyProducts': 'You can add up to 50 different products per order.',
      'cart.storageWarning': 'The cart could not be saved in this browser and may be lost when you close the page.'
    }
  };
  const t = (key, vars = {}) => {
    const translated = I18N[key];
    const value = typeof translated === 'string' && translated !== key
      ? translated
      : (CART_COPY[LANG]?.[key] ?? key);
    return String(value).replace(/\{(\w+)\}/g, (_, keyName) => (keyName in vars ? vars[keyName] : `{${keyName}}`));
  };
  const STORAGE_KEY = 'shannana.purchase-cart.v1';
  const MAX_PRODUCTS = 50;
  const MAX_QTY = 1_000_000;
  const nf = new Intl.NumberFormat('en-US');
  const safeImage = (value) => (/^\/(?!\/)/.test(value || '') || /^https:\/\//.test(value || '')) ? value : '';
  const esc = (value) => String(value ?? '').replace(/[&<>"']/g, (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[char]));
  const cleanInt = (value) => {
    const number = Number(value);
    return Number.isSafeInteger(number) && number >= 0 ? Math.min(number, MAX_QTY) : 0;
  };

  const state = {
    items: readCart(),
    step: 'cart',
    formToken: '',
    tokenIssuedAt: 0,
    submitting: false,
    toastTimer: null,
  };

  function normalizeSavedItem(item) {
    if (!item || !Number.isSafeInteger(Number(item.id)) || Number(item.id) < 1) return null;
    const id = Number(item.id);
    const cartonUnits = Number.isSafeInteger(Number(item.cartonUnits)) && Number(item.cartonUnits) > 0
      ? Number(item.cartonUnits)
      : null;
    const cartonQuantity = cleanInt(item.cartonQuantity);
    const unitQuantity = cleanInt(item.unitQuantity);
    if (cartonQuantity === 0 && unitQuantity === 0) return null;
    return {
      id,
      nameAr: String(item.nameAr || item.name || '').slice(0, 150),
      nameEn: String(item.nameEn || '').slice(0, 150),
      slug: String(item.slug || '').slice(0, 150),
      imageUrl: safeImage(String(item.imageUrl || '')),
      tone: ['red', 'gold', 'rose', 'green'].includes(item.tone) ? item.tone : 'gold',
      cartonUnits,
      cartonQuantity: cartonUnits ? cartonQuantity : 0,
      unitQuantity,
    };
  }

  function readCart() {
    try {
      const parsed = JSON.parse(localStorage.getItem(STORAGE_KEY) || '[]');
      if (!Array.isArray(parsed)) return [];
      const seen = new Set();
      return parsed.map(normalizeSavedItem).filter((item) => {
        if (!item || seen.has(item.id)) return false;
        seen.add(item.id);
        return true;
      }).slice(0, MAX_PRODUCTS);
    } catch {
      return [];
    }
  }

  function persist() {
    try {
      // Only product IDs, display snapshots and quantities are stored. Checkout fields stay in memory.
      localStorage.setItem(STORAGE_KEY, JSON.stringify(state.items));
    } catch (error) {
      console.warn('[cart] Local storage is unavailable; cart will remain in memory for this page.', error);
      showToast(t('cart.storageWarning'));
    }
    updateBadge();
  }

  function totalFor(item) {
    return (item.cartonQuantity * (item.cartonUnits || 0)) + item.unitQuantity;
  }

  function totalUnits() {
    return state.items.reduce((total, item) => total + totalFor(item), 0);
  }

  function currentName(item) {
    return LANG === 'en' ? (item.nameEn || item.nameAr || '') : (item.nameAr || item.nameEn || '');
  }

  function updateBadge() {
    const count = totalUnits();
    document.querySelectorAll('[data-cart-count]').forEach((badge) => {
      badge.textContent = count > 99 ? '99+' : String(count);
      badge.hidden = count === 0;
    });
  }

  function shellMarkup() {
    return `
      <dialog class="sf-dlg sf-cart-dialog" id="sfCartDialog" aria-labelledby="sfCartTitle">
        <div class="sf-cart-shell">
          <div class="sf-head sf-cart-head">
            <div><h2 id="sfCartTitle">${esc(t('cart.title'))}</h2><p class="sf-cart-subtitle">${esc(t('cart.subtitle'))}</p></div>
            <button class="sf-x" type="button" data-cart-close aria-label="${esc(t('cart.close'))}">×</button>
          </div>

          <section class="sf-body sf-cart-body" data-cart-step="cart">
            <div class="sf-cart-empty" id="sfCartEmpty" hidden>
              <span class="sf-cart-empty-icon" aria-hidden="true">🛒</span>
              <h3>${esc(t('cart.empty'))}</h3>
              <p>${esc(t('cart.emptyHint'))}</p>
              <button class="btn btn-secondary" type="button" data-cart-close>${esc(t('cart.keepBrowsing'))}</button>
            </div>
            <div id="sfCartItems" class="sf-cart-items"></div>
            <div id="sfCartSummary" class="sf-cart-summary" hidden></div>
            <div class="sf-alert" id="sfCartAlert" role="alert" aria-live="polite" hidden></div>
            <div class="sf-foot sf-cart-foot" id="sfCartFoot">
              <span class="sf-note">${esc(t('cart.noPrices'))}</span>
              <button class="btn btn-primary" id="sfCartContinue" type="button">${esc(t('cart.checkout'))}</button>
            </div>
          </section>

          <section class="sf-body sf-cart-body" data-cart-step="checkout" hidden>
            <div class="sf-cart-stepbar">
              <button class="link-btn" type="button" data-cart-back>← ${esc(t('cart.back'))}</button>
              <div><h3>${esc(t('cart.detailsTitle'))}</h3><p>${esc(t('cart.detailsLead'))}</p></div>
            </div>
            <div class="sf-alert" id="sfCheckoutAlert" role="alert" aria-live="polite" hidden></div>
            <form id="sfCheckoutForm" novalidate>
              <input type="hidden" name="formToken" value="">
              <input type="hidden" name="lang" value="${LANG}">
              <div class="sf-hp" aria-hidden="true"><label for="cart_website">Leave this field empty</label><input id="cart_website" name="website" tabindex="-1" autocomplete="off"></div>
              <div class="sf-grid">
                <div class="sf-field" data-field="customerName"><label for="cart_customerName">${esc(t('cart.contactName'))} *</label><input id="cart_customerName" name="customerName" autocomplete="name" minlength="2" maxlength="100" required><div class="sf-err"></div></div>
                <div class="sf-field" data-field="phone"><label for="cart_phone">${esc(t('cart.phone'))} *</label><input id="cart_phone" name="phone" type="tel" dir="ltr" autocomplete="tel" maxlength="30" placeholder="+963…" required><div class="sf-err"></div></div>
                <div class="sf-field" data-field="businessName"><label for="cart_businessName">${esc(t('cart.businessName'))} *</label><input id="cart_businessName" name="businessName" autocomplete="organization" minlength="2" maxlength="150" required><div class="sf-err"></div></div>
                <div class="sf-field" data-field="city"><label for="cart_city">${esc(t('cart.city'))} *</label><input id="cart_city" name="city" autocomplete="address-level2" minlength="2" maxlength="100" required><div class="sf-err"></div></div>
                <div class="sf-field" data-field="area"><label for="cart_area">${esc(t('cart.area'))} <small>${esc(t('cart.optional'))}</small></label><input id="cart_area" name="area" autocomplete="address-level3" maxlength="100"><div class="sf-err"></div></div>
                <div class="sf-field" data-field="email"><label for="cart_email">${esc(t('cart.email'))} <small>${esc(t('cart.optional'))}</small></label><input id="cart_email" name="email" type="email" dir="ltr" autocomplete="email" maxlength="254"><div class="sf-err"></div></div>
                <div class="sf-field" data-field="whatsapp"><label for="cart_whatsapp">${esc(t('cart.whatsapp'))} <small>${esc(t('cart.optional'))}</small></label><input id="cart_whatsapp" name="whatsapp" type="tel" dir="ltr" maxlength="30" placeholder="+963…"><div class="sf-err"></div></div>
              </div>
              <div class="sf-field" data-field="address"><label for="cart_address">${esc(t('cart.address'))} <small>${esc(t('cart.optional'))}</small></label><textarea id="cart_address" name="address" rows="2" maxlength="300"></textarea><div class="sf-err"></div></div>
              <div class="sf-field" data-field="customerNote"><label for="cart_customerNote">${esc(t('cart.note'))} <small>${esc(t('cart.optional'))}</small></label><textarea id="cart_customerNote" name="customerNote" rows="3" maxlength="2000"></textarea><div class="sf-err"></div></div>
              <div class="sf-cart-checkout-summary" id="sfCheckoutSummary"></div>
              <div class="sf-foot sf-cart-foot">
                <span class="sf-note">${esc(t('cart.requiredNote'))}</span>
                <button class="btn btn-primary" id="sfCartSubmit" type="submit">${esc(t('cart.submit'))}</button>
              </div>
            </form>
          </section>

          <section class="sf-body sf-cart-success" data-cart-step="success" hidden>
            <div class="tick" aria-hidden="true">✓</div>
            <h3>${esc(t('cart.successTitle'))}</h3>
            <p>${esc(t('cart.successText'))}</p>
            <div class="sf-cart-reference"><span>${esc(t('cart.reference'))}</span><strong id="sfOrderReference"></strong></div>
            <a class="btn btn-primary sf-cart-whatsapp-link" id="sfOrderWhatsApp" href="#" target="_blank" rel="noopener noreferrer" hidden>${esc(t('cart.whatsappOpen'))}</a>
            <p class="sf-cart-whatsapp-hint" id="sfOrderWhatsAppHint" hidden>${esc(t('cart.whatsappHint'))}</p>
            <button class="btn btn-secondary" type="button" data-cart-close>${esc(t('cart.close'))}</button>
          </section>
        </div>
      </dialog>
      <div class="sf-cart-toast" id="sfCartToast" role="status" aria-live="polite" aria-atomic="true"></div>`;
  }

  document.body.insertAdjacentHTML('beforeend', shellMarkup());

  const dialog = document.getElementById('sfCartDialog');
  const itemsEl = document.getElementById('sfCartItems');
  const emptyEl = document.getElementById('sfCartEmpty');
  const summaryEl = document.getElementById('sfCartSummary');
  const cartAlert = document.getElementById('sfCartAlert');
  const checkoutAlert = document.getElementById('sfCheckoutAlert');
  const checkoutForm = document.getElementById('sfCheckoutForm');
  const submitButton = document.getElementById('sfCartSubmit');
  const checkoutSummary = document.getElementById('sfCheckoutSummary');
  const toast = document.getElementById('sfCartToast');

  function showToast(message) {
    if (!message) return;
    toast.textContent = message;
    toast.classList.add('is-visible');
    window.clearTimeout(state.toastTimer);
    state.toastTimer = window.setTimeout(() => toast.classList.remove('is-visible'), 2600);
  }

  function setStep(step) {
    state.step = step;
    dialog.querySelectorAll('[data-cart-step]').forEach((section) => {
      section.hidden = section.dataset.cartStep !== step;
    });
  }

  function openCart() {
    renderCart();
    setStep('cart');
    if (!dialog.open) dialog.showModal();
  }

  function closeCart() {
    if (dialog.open) dialog.close();
  }

  function renderCart() {
    updateBadge();
    emptyEl.hidden = state.items.length > 0;
    itemsEl.hidden = state.items.length === 0;
    summaryEl.hidden = state.items.length === 0;
    document.getElementById('sfCartFoot').hidden = state.items.length === 0;

    if (!state.items.length) {
      itemsEl.replaceChildren();
      summaryEl.replaceChildren();
      return;
    }

    itemsEl.innerHTML = state.items.map((item) => {
      const name = currentName(item);
      const total = totalFor(item);
      const image = item.imageUrl
        ? `<img src="${esc(item.imageUrl)}" alt="" loading="lazy">`
        : `<span class="sf-cart-placeholder" aria-hidden="true">${esc(name.slice(0, 1))}</span>`;
      const cartonInfo = item.cartonUnits
        ? `<small class="sf-cart-carton-info">${esc(t('cart.cartonSize', { count: nf.format(item.cartonUnits) }))}</small>`
        : `<small class="sf-cart-carton-info is-muted">${esc(t('cart.noCartons'))}</small>`;
      return `
        <article class="sf-cart-item" data-cart-item="${item.id}">
          <div class="sf-cart-item-top">
            <div class="sf-cart-thumb">${image}</div>
            <div class="sf-cart-item-name"><strong>${esc(name)}</strong>${cartonInfo}</div>
            <button class="sf-cart-remove" type="button" data-cart-remove="${item.id}" aria-label="${esc(t('cart.remove'))}">×</button>
          </div>
          <div class="sf-cart-quantities">
            <label><span>${esc(t('cart.cartons'))}</span><input type="number" min="0" max="${MAX_QTY}" step="1" inputmode="numeric" data-cart-qty="cartonQuantity" data-product-id="${item.id}" value="${item.cartonQuantity}" ${item.cartonUnits ? '' : 'disabled'}></label>
            <label><span>${esc(t('cart.units'))}</span><input type="number" min="0" max="${MAX_QTY}" step="1" inputmode="numeric" data-cart-qty="unitQuantity" data-product-id="${item.id}" value="${item.unitQuantity}"></label>
          </div>
          <div class="sf-cart-item-total"><span>${esc(t('cart.itemTotal'))}</span><strong>${nf.format(total)} ${esc(t('cart.unitsWord'))}</strong></div>
        </article>`;
    }).join('');

    summaryEl.innerHTML = `
      <div><span>${esc(t('cart.productCount'))}</span><strong>${nf.format(state.items.length)}</strong></div>
      <div><span>${esc(t('cart.totalUnits'))}</span><strong>${nf.format(totalUnits())} ${esc(t('cart.unitsWord'))}</strong></div>`;
    document.getElementById('sfCartContinue').disabled = state.items.length === 0;
  }

  function renderCheckoutSummary() {
    checkoutSummary.innerHTML = `
      <div class="sf-cart-summary-heading">${esc(t('cart.orderSummary'))}</div>
      ${state.items.map((item) => {
        const cartonPart = item.cartonQuantity > 0 ? `${nf.format(item.cartonQuantity)} ${esc(t('cart.cartonsShort'))}` : '';
        const unitsPart = item.unitQuantity > 0 ? `${nf.format(item.unitQuantity)} ${esc(t('cart.unitsShort'))}` : '';
        return `<div class="sf-cart-summary-row"><span>${esc(currentName(item))}</span><strong>${[cartonPart, unitsPart].filter(Boolean).join(' + ')}</strong></div>`;
      }).join('')}
      <div class="sf-cart-summary-total"><span>${esc(t('cart.totalUnits'))}</span><strong>${nf.format(totalUnits())} ${esc(t('cart.unitsWord'))}</strong></div>`;
  }

  function addProductFromButton(button) {
    const data = button.dataset;
    const id = Number(data.productId);
    if (!Number.isSafeInteger(id) || id < 1) return;
    const cartonUnitsRaw = Number(data.cartonUnits);
    const cartonUnits = Number.isSafeInteger(cartonUnitsRaw) && cartonUnitsRaw > 0 ? cartonUnitsRaw : null;
    const nameAr = (data.productNameAr || data.productName || '').trim();
    const nameEn = (data.productNameEn || '').trim();
    const existing = state.items.find((item) => item.id === id);
    if (!existing && state.items.length >= MAX_PRODUCTS) {
      showToast(t('cart.tooManyProducts'));
      return;
    }
    if (existing) {
      existing.nameAr = nameAr || existing.nameAr;
      existing.nameEn = nameEn || existing.nameEn;
      existing.slug = data.productSlug || existing.slug;
      existing.imageUrl = safeImage(data.productImage || existing.imageUrl);
      existing.tone = ['red', 'gold', 'rose', 'green'].includes(data.productTone) ? data.productTone : existing.tone;
      existing.cartonUnits = cartonUnits;
      if (!cartonUnits) existing.cartonQuantity = 0;
      existing.unitQuantity = Math.min(MAX_QTY, existing.unitQuantity + 1);
    } else {
      state.items.push({
        id,
        nameAr: nameAr || nameEn || '',
        nameEn,
        slug: String(data.productSlug || ''),
        imageUrl: safeImage(data.productImage || ''),
        tone: ['red', 'gold', 'rose', 'green'].includes(data.productTone) ? data.productTone : 'gold',
        cartonUnits,
        cartonQuantity: 0,
        unitQuantity: 1,
      });
    }
    persist();
    showToast(t('cart.added'));
  }

  async function openCheckout() {
    if (!state.items.length) return;
    cartAlert.hidden = true;
    checkoutAlert.hidden = true;
    renderCheckoutSummary();
    checkoutForm.reset();
    checkoutForm.elements.lang.value = LANG;
    checkoutForm.elements.website.value = '';
    checkoutForm.elements.formToken.value = '';
    state.formToken = '';
    state.tokenIssuedAt = 0;
    setStep('checkout');
    try {
      const response = await fetch(`/api/forms/token?lang=${LANG}`, { headers: { Accept: 'application/json' }, cache: 'no-store' });
      const body = await response.json();
      if (!response.ok || !body.success || !body.data?.token) throw new Error(body.message || t('cart.tokenError'));
      state.formToken = body.data.token;
      state.tokenIssuedAt = Date.now();
      checkoutForm.elements.formToken.value = state.formToken;
    } catch (error) {
      showAlert(checkoutAlert, error.message || t('cart.tokenError'));
    }
  }

  function showAlert(element, message) {
    element.textContent = message;
    element.hidden = false;
  }

  function clearFieldErrors() {
    checkoutForm.querySelectorAll('.sf-field.invalid').forEach((field) => field.classList.remove('invalid'));
    checkoutForm.querySelectorAll('.sf-err').forEach((element) => { element.textContent = ''; });
  }

  function showServerErrors(body) {
    clearFieldErrors();
    const errors = Array.isArray(body?.errors) ? body.errors : [];
    errors.forEach((error) => {
      const fieldName = String(error.field || '').split('.')[0];
      const field = checkoutForm.querySelector(`[data-field="${CSS.escape(fieldName)}"]`);
      if (!field) return;
      field.classList.add('invalid');
      const output = field.querySelector('.sf-err');
      if (output) output.textContent = error.message || '';
    });
    const detail = errors.map((error) => error.message).filter(Boolean).join(' · ');
    showAlert(checkoutAlert, [body?.message, detail].filter(Boolean).join(' — ') || t('cart.genericError'));
  }

  function formValue(name) {
    return String(checkoutForm.elements[name]?.value ?? '').trim();
  }

  const wait = (ms) => new Promise((resolve) => window.setTimeout(resolve, ms));

  async function submitOrder(event) {
    event.preventDefault();
    if (state.submitting || !state.items.length) return;
    clearFieldErrors();
    checkoutAlert.hidden = true;
    if (!checkoutForm.reportValidity()) return;
    if (!state.formToken) {
      showAlert(checkoutAlert, t('cart.tokenError'));
      return;
    }

    state.submitting = true;
    submitButton.disabled = true;
    submitButton.textContent = t('cart.sending');
    try {
      // The current form guard requires at least 2.5 seconds between issuing and using the token.
      const age = Date.now() - state.tokenIssuedAt;
      if (age < 2700) {
        showAlert(checkoutAlert, t('cart.securityWait'));
        await wait(2700 - age);
      }

      const payload = {
        lang: LANG,
        formToken: state.formToken,
        website: formValue('website'),
        customerName: formValue('customerName'),
        phone: formValue('phone'),
        businessName: formValue('businessName'),
        city: formValue('city'),
        area: formValue('area'),
        address: formValue('address'),
        email: formValue('email'),
        whatsapp: formValue('whatsapp'),
        customerNote: formValue('customerNote'),
        items: state.items.map((item) => ({
          productId: item.id,
          cartonQuantity: item.cartonQuantity,
          unitQuantity: item.unitQuantity,
        })),
      };

      const response = await fetch('/api/purchase-orders', {
        method: 'POST',
        headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });
      let body;
      try { body = await response.json(); } catch { body = {}; }
      if (!response.ok || !body.success || !body.data?.order?.reference) {
        showServerErrors(body);
        return;
      }

      // Clear only after the backend has confirmed that the order was saved.
      state.items = [];
      persist();
      checkoutForm.reset();
      document.getElementById('sfOrderReference').textContent = body.data.order.reference;
      const waLink = document.getElementById('sfOrderWhatsApp');
      const waHint = document.getElementById('sfOrderWhatsAppHint');
      waLink.hidden = true;
      waHint.hidden = true;
      try {
        const candidate = new URL(String(body.data.order.whatsappUrl || ''), window.location.origin);
        if (candidate.protocol === 'https:' && candidate.hostname === 'wa.me') {
          waLink.href = candidate.href;
          waLink.hidden = false;
          waHint.hidden = false;
        } else {
          waHint.textContent = t('cart.whatsappUnavailable');
          waHint.hidden = false;
        }
      } catch {
        waHint.textContent = t('cart.whatsappUnavailable');
        waHint.hidden = false;
      }
      setStep('success');
    } catch (error) {
      console.error('[cart] Order submission failed:', error);
      showAlert(checkoutAlert, t('cart.genericError'));
    } finally {
      state.submitting = false;
      submitButton.disabled = false;
      submitButton.textContent = t('cart.submit');
    }
  }

  const cartToggle = document.getElementById('cartToggle');
  cartToggle?.setAttribute('aria-label', t('cart.open'));
  cartToggle?.addEventListener('click', openCart);
  document.addEventListener('click', (event) => {
    const addButton = event.target.closest?.('[data-cart-add]');
    if (addButton) {
      event.preventDefault();
      addProductFromButton(addButton);
      return;
    }
    if (event.target.closest?.('[data-cart-close]')) closeCart();
    if (event.target.closest?.('[data-cart-back]')) setStep('cart');
    if (event.target.closest?.('#sfCartContinue')) openCheckout();
    const removeButton = event.target.closest?.('[data-cart-remove]');
    if (removeButton) {
      const id = Number(removeButton.dataset.cartRemove);
      state.items = state.items.filter((item) => item.id !== id);
      persist();
      renderCart();
    }
  });

  dialog.addEventListener('change', (event) => {
    const input = event.target.closest?.('[data-cart-qty]');
    if (!input) return;
    const item = state.items.find((row) => row.id === Number(input.dataset.productId));
    if (!item) return;
    const key = input.dataset.cartQty;
    if (!['cartonQuantity', 'unitQuantity'].includes(key)) return;
    item[key] = cleanInt(input.value);
    if (!item.cartonUnits) item.cartonQuantity = 0;
    if (item.cartonQuantity === 0 && item.unitQuantity === 0) {
      state.items = state.items.filter((row) => row.id !== item.id);
    }
    persist();
    renderCart();
  });

  dialog.addEventListener('click', (event) => {
    if (event.target === dialog) closeCart();
  });
  dialog.addEventListener('close', () => {
    state.submitting = false;
    submitButton.disabled = false;
    submitButton.textContent = t('cart.submit');
  });
  checkoutForm.addEventListener('submit', submitOrder);

  const productAddButton = document.getElementById('productAddToCart');
  if (productAddButton) {
    productAddButton.textContent = t('cart.addToCart');
    productAddButton.setAttribute('aria-label', t('cart.addToCart'));
  }

  updateBadge();
})();
