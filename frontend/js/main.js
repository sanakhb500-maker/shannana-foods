    // لغة الصفحة ونصوص الواجهة تأتي من الخادم (الوسم <html lang> وكتلة JSON #sf-i18n في القالب).
    const LANG = document.documentElement.lang === 'en' ? 'en' : 'ar';
    const I18N = (() => { try { return JSON.parse(document.getElementById('sf-i18n').textContent); } catch { return {}; } })();
    // Cart-card copy has a local fallback in case the server translation payload
    // does not yet include the cart.* entries (the cart modal has the full fallback).
    const CART_COPY = LANG === 'en'
      ? { 'cart.addToCart': 'Add to cart' }
      : { 'cart.addToCart': 'أضف إلى السلة' };
    const t = (key, vars = {}) => {
      const translated = I18N[key];
      const value = typeof translated === 'string' && translated !== key
        ? translated
        : (CART_COPY[key] ?? key);
      return String(value).replace(/\{(\w+)\}/g, (_, k) => (k in vars ? vars[k] : `{${k}}`));
    };

    const filtersEl = document.getElementById('filters');
    const gridEl = document.getElementById('productsGrid');
    let catalog = { categories: [], products: [] };
    // The homepage starts with a compact featured selection. The query-string
    // mode exposes the complete active catalog without relying on an unverified route.
    let allProductsMode = new URLSearchParams(window.location.search).get('catalog') === 'all';
    const viewAllLink = document.getElementById('viewAllProducts');

    // Product data comes from the database, so everything is escaped before it reaches innerHTML.
    const esc = v => String(v ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const safeImg = u => (/^\/(?!\/)/.test(u) || /^https?:\/\//.test(u)) ? u : '';
    const TONES = ['red', 'gold', 'rose', 'green'];

    // رابط صفحة تفاصيل المنتج: /products/<slug> بالعربية و/en/products/<slug> بالإنجليزية
    const productHref = p => (LANG === 'en' ? '/en' : '') + '/products/' + encodeURIComponent(p.slug);

    async function api(path) {
      const ctrl = new AbortController();
      const timer = setTimeout(() => ctrl.abort(), 6000);
      try {
        const res = await fetch('/api' + path + (path.includes('?') ? '&' : '?') + 'lang=' + LANG, { headers: { Accept: 'application/json' }, signal: ctrl.signal });
        const body = await res.json();
        if (!res.ok || !body.success) throw new Error(body.message || ('HTTP ' + res.status));
        return body.data;
      } finally { clearTimeout(timer); }
    }

    async function loadCatalog() {
      if (!gridEl || !filtersEl) return; // صفحات أخرى (مثل صفحة المنتج) تحمّل هذا الملف بدون قسم المنتجات
      try {
        const [categories, products] = await Promise.all([api('/categories'), api('/products')]);
        catalog = { categories, products };
      } catch (err) {
        console.warn('[catalog] API unavailable:', err.message);
        gridEl.dataset.source = 'error';
        filtersEl.innerHTML = '';
        gridEl.innerHTML = `<p class="catalog-msg" style="grid-column:1/-1;text-align:center;color:#6E6762;padding:24px 0;">${esc(t('catalog.error'))}<br><button class="btn btn-secondary" type="button" data-retry style="margin-top:12px">${esc(t('catalog.retry'))}</button></p>`;
        gridEl.querySelector('[data-retry]').addEventListener('click', loadCatalog);
        return;
      }
      gridEl.dataset.source = 'api';
      buildFilters();
      renderProducts();
    }

    // اعرض 8 منتجات كحد أقصى مع إعطاء أولوية للمنتجات المحددة كمميزة من لوحة الإدارة.
    // إذا كان عدد المنتجات المميزة أقل من 8، تُستكمل المساحات من بقية الكتالوج حسب ترتيبه.
    function getFeaturedProducts() {
      const prioritized = [
        ...catalog.products.filter(p => p.isFeatured),
        ...catalog.products.filter(p => !p.isFeatured),
      ];
      return prioritized.slice(0, 8);
    }

    function buildFilters() {
      filtersEl.innerHTML = '';
      // Always keep every active category visible, even if none of the featured
      // products belongs to it. Filtering then shows matching items or the empty state.
      const items = [{ name: t('catalog.all'), slug: 'all' }, ...catalog.categories];
      items.forEach((item, index) => {
        const btn = document.createElement('button');
        btn.className = 'filter-btn' + (index === 0 ? ' active' : '');
        btn.textContent = item.name;
        btn.type = 'button';
        btn.setAttribute('aria-pressed', String(index === 0));
        btn.addEventListener('click', () => {
          filtersEl.querySelectorAll('.filter-btn').forEach(b => { b.classList.remove('active'); b.setAttribute('aria-pressed', 'false'); });
          btn.classList.add('active');
          btn.setAttribute('aria-pressed', 'true');
          renderProducts(item.slug);
        });
        filtersEl.appendChild(btn);
      });
    }

    // Toggle between featured products and the full catalog.
    if (viewAllLink) {
      viewAllLink.hidden = false;

      const updateViewAllLabel = () => {
        const label = allProductsMode
          ? viewAllLink.dataset.labelFeatured
          : viewAllLink.dataset.labelAll;

        viewAllLink.textContent = label || (
          allProductsMode
            ? (LANG === 'en' ? 'Show featured products only' : 'عرض المنتجات المميزة فقط')
            : (LANG === 'en' ? 'View all products' : 'عرض جميع المنتجات')
        );
      };

    updateViewAllLabel();

    viewAllLink.addEventListener('click', (event) => {
      event.preventDefault();

      allProductsMode = !allProductsMode;

      // Reset the selected category to "All".
      const allFilter = filtersEl.querySelector('.filter-btn');
      filtersEl.querySelectorAll('.filter-btn').forEach(btn => {
        btn.classList.remove('active');
        btn.setAttribute('aria-pressed', 'false');
      });

      if (allFilter) {
        allFilter.classList.add('active');
        allFilter.setAttribute('aria-pressed', 'true');
      }

      updateViewAllLabel();
      renderProducts('all');
    });
  }

    function renderProducts(slug = 'all') {
      // Homepage: show up to 8 prioritized products.
      // ?catalog=all: show the full active catalog.
      // التصنيف المختار يعرض جميع منتجاته دائمًا.
      // وضع "الكل" يعرض 8 منتجات في الرئيسية أو الكتالوج الكامل عند التوسيع.
      const source = (allProductsMode || slug !== 'all')
        ? catalog.products
        : getFeaturedProducts();

      const list = slug === 'all'
        ? source
        : source.filter(p => p.category.slug === slug);
      if (!list.length) {
        gridEl.innerHTML = `<p style="grid-column:1/-1;text-align:center;color:#6E6762;padding:24px 0;">${esc(t('catalog.empty'))}</p>`;
        return;
      }
      gridEl.innerHTML = list.map((p, i) => `
        <article class="product-card reveal" style="--d:${Math.min((i % 4) * 70, 210)}ms">
          <div class="product-image-wrap tone-${TONES.includes(p.tone) ? p.tone : 'gold'}">
            <a class="product-link" href="${esc(productHref(p))}"><img class="product-image" src="${esc(safeImg(p.imageUrl))}" alt="${esc(p.name)}" loading="lazy"></a>
          </div>
          <div class="product-body">
            <div class="product-heading">
              <h3 class="product-title"><a href="${esc(productHref(p))}">${esc(p.name)}</a></h3>
              <span class="category-badge">${esc(p.category.name)}</span>
            </div>
            <p class="product-desc">${esc(p.description)}</p>
            <div class="product-footer">
              <button class="product-add-btn" type="button" data-cart-add
                data-product-id="${esc(p.id)}"
                data-product-name="${esc(p.name)}"
                data-product-name-ar="${esc(p.nameAr || p.name)}"
                data-product-name-en="${esc(p.nameEn || '')}"
                data-product-slug="${esc(p.slug)}"
                data-product-image="${esc(safeImg(p.imageUrl))}"
                data-product-tone="${esc(p.tone)}"
                data-carton-units="${p.cartonUnits == null ? '' : esc(p.cartonUnits)}">${esc(t('cart.addToCart'))}</button>
            </div>
          </div>
        </article>
      `).join('');
      initReveal(gridEl.querySelectorAll('.reveal'));
    }

    let revealObserver = null;
    function initReveal(nodes) {
      if (!('IntersectionObserver' in window)) { nodes.forEach(el => el.classList.add('in')); return; }
      revealObserver = revealObserver || new IntersectionObserver(entries => {
        entries.forEach(entry => {
          if (entry.isIntersecting) {
            entry.target.classList.add('in');
            revealObserver.unobserve(entry.target);
          }
        });
      }, { threshold: .12, rootMargin: '0px 0px -40px 0px' });
      nodes.forEach(el => revealObserver.observe(el));
    }

    loadCatalog();

    const header = document.getElementById('header');
    const onScroll = () => header.classList.toggle('scrolled', window.scrollY > 20);
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });

    const menuBtn = document.getElementById('menuBtn');
    const mobilePanel = document.getElementById('mobilePanel');
    menuBtn.addEventListener('click', () => {
      const open = mobilePanel.classList.toggle('open');
      menuBtn.setAttribute('aria-expanded', String(open));
    });
    document.querySelectorAll('.mobile-panel a').forEach(link => link.addEventListener('click', () => {
      mobilePanel.classList.remove('open');
      menuBtn.setAttribute('aria-expanded', 'false');
    }));

    // Extra elements joining the same reveal system (headings, why section)
    document.querySelectorAll('.section-head, .about-copy, .about-visual, .why-panel, .quote-card').forEach(el => el.classList.add('reveal'));
    document.querySelectorAll('.why-item').forEach((el, i) => { el.classList.add('reveal'); el.style.setProperty('--d', (i * 90) + 'ms'); });
    initReveal(document.querySelectorAll('.reveal'));

    /* ---------- Premium motion (fine-pointer hover devices only) ---------- */
    const finePointer = window.matchMedia('(hover: hover) and (pointer: fine)');
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const motionOK = e => e.pointerType === 'mouse' && finePointer.matches && !reduceMotion.matches;
    const clamp = (v, m = 1) => Math.max(-m, Math.min(m, v));

    // Hero parallax + card tilt: one lerped (hx, hy) pair in [-1, 1] drives every layer via CSS vars
    const heroEl = document.getElementById('home');
    if (heroEl) {
      let tx = 0, ty = 0, cx = 0, cy = 0, raf = 0;
      const tick = () => {
        cx += (tx - cx) * .09;
        cy += (ty - cy) * .09;
        const done = Math.abs(tx - cx) < .002 && Math.abs(ty - cy) < .002;
        if (done) { cx = tx; cy = ty; }
        heroEl.style.setProperty('--hx', cx.toFixed(4));
        heroEl.style.setProperty('--hy', cy.toFixed(4));
        raf = done ? 0 : requestAnimationFrame(tick);
      };
      const kick = () => { if (!raf) raf = requestAnimationFrame(tick); };
      heroEl.addEventListener('pointermove', e => {
        if (!motionOK(e)) return;
        const r = heroEl.getBoundingClientRect();
        tx = clamp(((e.clientX - r.left) / r.width - .5) * 2);
        ty = clamp(((e.clientY - r.top) / r.height - .5) * 2);
        kick();
      });
      heroEl.addEventListener('pointerleave', () => { tx = 0; ty = 0; kick(); });
    }

    // Product card tilt + independent image shift (event delegation: survives filter re-renders)
    {
      let pending = null, cardRaf = 0;
      const flush = () => {
        cardRaf = 0;
        if (!pending) return;
        const { card, px, py } = pending;
        pending = null;
        card.style.setProperty('--ry', (px * 5).toFixed(2) + 'deg');
        card.style.setProperty('--rx', (-py * 5).toFixed(2) + 'deg');
        const img = card.querySelector('.product-image');
        if (img) {
          img.style.setProperty('--ix', (px * 7).toFixed(1) + 'px');
          img.style.setProperty('--iy', (py * 6).toFixed(1) + 'px');
        }
      };
      const resetCard = card => {
        ['--rx', '--ry'].forEach(p => card.style.removeProperty(p));
        const img = card.querySelector('.product-image');
        if (img) ['--ix', '--iy'].forEach(p => img.style.removeProperty(p));
      };
      gridEl?.addEventListener('pointermove', e => {
        if (!motionOK(e)) return;
        const card = e.target.closest('.product-card');
        if (!card) return;
        const r = card.getBoundingClientRect();
        pending = { card, px: clamp(((e.clientX - r.left) / r.width - .5) * 2), py: clamp(((e.clientY - r.top) / r.height - .5) * 2) };
        if (!cardRaf) cardRaf = requestAnimationFrame(flush);
      });
      gridEl?.addEventListener('pointerleave', e => {
        if (e.target.classList && e.target.classList.contains('product-card')) {
          if (pending && pending.card === e.target) pending = null;
          resetCard(e.target);
        }
      }, true);
    }

    // Limited magnetic effect on buttons (max 6px, uses the `translate` property so click target stays put)
    document.querySelectorAll('.btn, .header-cta').forEach(el => {
      el.addEventListener('pointermove', e => {
        if (!motionOK(e)) return;
        const r = el.getBoundingClientRect();
        const dx = clamp((e.clientX - (r.left + r.width / 2)) * .18, 6);
        const dy = clamp((e.clientY - (r.top + r.height / 2)) * .18, 6);
        el.style.translate = dx.toFixed(1) + 'px ' + dy.toFixed(1) + 'px';
      });
      el.addEventListener('pointerleave', () => { el.style.translate = ''; });
    });

    /* ---------- Hero scroll-exit progress + lazy-loaded 3D hero ---------- */
    {
      const heroBox = document.getElementById('home');
      if (heroBox && !window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
        let ticking = false;
        const upd = () => {
          ticking = false;
          const p = Math.min(1, Math.max(0, window.scrollY / (heroBox.offsetHeight * .85)));
          heroBox.style.setProperty('--hp', p.toFixed(3));
        };
        window.addEventListener('scroll', () => { if (!ticking) { ticking = true; requestAnimationFrame(upd); } }, { passive: true });
        upd();
      }
      const heroCard = document.querySelector('.hero-card');
      const conn = navigator.connection;
      if (heroBox && heroCard && !(conn && conn.saveData)) {
        const load = () => import('./hero3d.js')
          .then(m => m.initHero3D({ hero: heroBox, card: heroCard }))
          .catch(err => console.warn('[hero3d] using 2D fallback:', err));
        const idle = () => ('requestIdleCallback' in window) ? requestIdleCallback(load, { timeout: 2000 }) : setTimeout(load, 300);
        if (document.readyState === 'complete') idle(); else window.addEventListener('load', idle, { once: true });
      }
    }

    /* ---------- Footer logo: make the (transparent) lettering white ----------
       In the PNG the lettering is see-through, so on the dark footer it shows the dark background.
       Fill transparent areas that are enclosed by the logo (not the outside) with white. */
    function fillEnclosedWhite(data, w, h, r) {
      const N = w * h, solid = new Uint8Array(N);
      for (let i = 0; i < N; i++) solid[i] = data[i * 4 + 3] >= 128 ? 1 : 0;
      const dilate = (src, rad) => {
        const tmp = new Uint8Array(N), out = new Uint8Array(N);
        for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
          let v = 0;
          for (let k = -rad; k <= rad && !v; k++) { const xx = x + k; if (xx >= 0 && xx < w && src[y * w + xx]) v = 1; }
          tmp[y * w + x] = v;
        }
        for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
          let v = 0;
          for (let k = -rad; k <= rad && !v; k++) { const yy = y + k; if (yy >= 0 && yy < h && tmp[yy * w + x]) v = 1; }
          out[y * w + x] = v;
        }
        return out;
      };
      const closed = dilate(solid, r);               // closes hairline gaps so letters don't "leak" outside
      const outside = new Uint8Array(N), stack = [];
      const push = i => { if (!closed[i] && !outside[i]) { outside[i] = 1; stack.push(i); } };
      for (let x = 0; x < w; x++) { push(x); push((h - 1) * w + x); }
      for (let y = 0; y < h; y++) { push(y * w); push(y * w + w - 1); }
      while (stack.length) {
        const i = stack.pop(), x = i % w, y = (i / w) | 0;
        if (x > 0) push(i - 1); if (x < w - 1) push(i + 1); if (y > 0) push(i - w); if (y < h - 1) push(i + w);
      }
      for (let pass = 0; pass <= r; pass++) {        // give the dilation margin back to the outside
        const add = [];
        for (let i = 0; i < N; i++) {
          if (solid[i] || outside[i]) continue;
          const x = i % w, y = (i / w) | 0;
          if ((x > 0 && outside[i - 1]) || (x < w - 1 && outside[i + 1]) || (y > 0 && outside[i - w]) || (y < h - 1 && outside[i + w])) add.push(i);
        }
        add.forEach(i => { outside[i] = 1; });
      }
      const hole = new Uint8Array(N);
      for (let i = 0; i < N; i++) hole[i] = !solid[i] && !outside[i] ? 1 : 0;
      const near = dilate(hole, 2);
      let filled = 0;
      for (let i = 0; i < N; i++) {
        const a = data[i * 4 + 3];
        if (a === 255 || !(hole[i] || (near[i] && solid[i]))) continue;
        const f = a / 255;                            // composite over white so edges stay smooth
        data[i * 4] = data[i * 4] * f + 255 * (1 - f);
        data[i * 4 + 1] = data[i * 4 + 1] * f + 255 * (1 - f);
        data[i * 4 + 2] = data[i * 4 + 2] * f + 255 * (1 - f);
        data[i * 4 + 3] = 255;
        filled++;
      }
      return filled;
    }
    {
      const logo = document.querySelector('.footer-logo');
      if (logo) {
        const fix = () => {
          if (logo.dataset.fixed || !logo.naturalWidth) return;
          logo.dataset.fixed = '1';
          try {
            const cw = Math.min(logo.naturalWidth, 700), ch = Math.round(logo.naturalHeight * cw / logo.naturalWidth);
            const cv = document.createElement('canvas');
            cv.width = cw; cv.height = ch;
            const ctx = cv.getContext('2d', { willReadFrequently: true });
            ctx.drawImage(logo, 0, 0, cw, ch);
            const im = ctx.getImageData(0, 0, cw, ch);
            if (fillEnclosedWhite(im.data, cw, ch, 3) > 0) { ctx.putImageData(im, 0, 0); logo.src = cv.toDataURL('image/png'); }
          } catch (e) { /* unreadable canvas: keep the original logo */ }
        };
        if (logo.complete) fix(); else logo.addEventListener('load', fix, { once: true });
      }
    }

    // Language switch keeps the current section (#hash) so shared/followed links land on the same place.
    {
      const links = document.querySelectorAll('[data-lang-link]');
      const syncHash = () => links.forEach(a => { a.hash = location.hash; });
      syncHash();
      window.addEventListener('hashchange', syncHash);
    }
