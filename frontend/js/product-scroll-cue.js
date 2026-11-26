(() => {
  const cue = document.querySelector('.pd-scroll-cue');
  const footer = document.querySelector('#contact');
  if (!cue || !footer) return;

  // Hide the floating arrow shortly before the footer enters the viewport.
  // The extra 72px prevents the arrow from appearing over the footer section.
  const HIDE_BEFORE_FOOTER_PX = 72;
  const setHidden = (hidden) => cue.classList.toggle('is-hidden', hidden);

  const isFooterNear = () => {
    const rect = footer.getBoundingClientRect();
    return rect.top <= window.innerHeight + HIDE_BEFORE_FOOTER_PX && rect.bottom > 0;
  };

  // Set the initial state immediately, avoiding a brief flash over the footer.
  setHidden(isFooterNear());

  if ('IntersectionObserver' in window) {
    const observer = new IntersectionObserver(([entry]) => {
      setHidden(Boolean(entry && entry.isIntersecting));
    }, {
      root: null,
      threshold: 0,
      rootMargin: `0px 0px ${HIDE_BEFORE_FOOTER_PX}px 0px`,
    });

    observer.observe(footer);
    return;
  }

  // Fallback for browsers without IntersectionObserver.
  let scheduled = false;
  const update = () => {
    if (scheduled) return;
    scheduled = true;
    window.requestAnimationFrame(() => {
      scheduled = false;
      setHidden(isFooterNear());
    });
  };

  window.addEventListener('scroll', update, { passive: true });
  window.addEventListener('resize', update);
  window.addEventListener('load', update, { once: true });
})();
