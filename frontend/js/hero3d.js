/* Shannana Foods — 3D hero scene (Three.js, local vendor build).
   The pack is built from the existing HeroProduct.png: its silhouette is read from the image alpha and
   "inflated" into a real volume (front + back shell) that is lit and shaded in 3D, so it keeps real
   depth when the camera orbits. It is NOT a scanned/modelled 3D pack: depth is an estimate from the
   silhouette, which is why the camera/drag range is limited. */
import * as THREE from './vendor/three.module.min.js';

const clamp01 = v => Math.min(1, Math.max(0, v));
const clamp = (v, m) => Math.max(-m, Math.min(m, v));
const easeOut = t => 1 - Math.pow(1 - t, 3);

function radialTexture(stops) {
  const c = document.createElement('canvas');
  c.width = c.height = 128;
  const g = c.getContext('2d');
  const grad = g.createRadialGradient(64, 64, 0, 64, 64, 64);
  stops.forEach(([o, col]) => grad.addColorStop(o, col));
  g.fillStyle = grad;
  g.fillRect(0, 0, 128, 128);
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

// Left/right silhouette edge (0..1 across the image width) for each of rows+1 horizontal slices
function readSilhouette(img, asp, rows) {
  const L = new Float32Array(rows + 1), R = new Float32Array(rows + 1);
  try {
    const cw = 256, ch = Math.max(64, Math.round(cw / asp));
    const cv = document.createElement('canvas');
    cv.width = cw; cv.height = ch;
    const g = cv.getContext('2d', { willReadFrequently: true });
    g.drawImage(img, 0, 0, cw, ch);
    const d = g.getImageData(0, 0, cw, ch).data;
    for (let j = 0; j <= rows; j++) {
      const y = Math.min(ch - 1, Math.max(0, Math.round((1 - j / rows) * (ch - 1))));
      let lo = -1, hi = -1;
      for (let x = 0; x < cw; x++) if (d[(y * cw + x) * 4 + 3] > 110) { if (lo < 0) lo = x; hi = x; }
      L[j] = lo < 0 ? 0.5 : lo / (cw - 1);
      R[j] = lo < 0 ? 0.5 : hi / (cw - 1);
    }
  } catch (e) { L.fill(0.2); R.fill(0.8); } // canvas unreadable: generic oval body
  return { L, R };
}

function smooth(a, radius = 2) {
  const out = new Float32Array(a.length);
  for (let i = 0; i < a.length; i++) {
    let s = 0, n = 0;
    for (let k = -radius; k <= radius; k++) { const j = i + k; if (j >= 0 && j < a.length) { s += a[j]; n++; } }
    out[i] = s / n;
  }
  return out;
}

// Height-field "inflation": z follows an ellipse across each row's silhouette width.
// Units: 1 = image width, so one uniform scale places the whole shell.
export function inflateGeometry(L, R, asp, cols, rows, depth = 0.75) {
  const pos = new Float32Array((cols + 1) * (rows + 1) * 3), uv = new Float32Array((cols + 1) * (rows + 1) * 2);
  const idx = [];
  for (let j = 0; j <= rows; j++) {
    const v = j / rows, cx = (L[j] + R[j]) / 2, hw = (R[j] - L[j]) / 2;
    for (let i = 0; i <= cols; i++) {
      const u = i / cols, k = j * (cols + 1) + i;
      const nx = hw > 1e-4 ? clamp((u - cx) / hw, 1) : 1;
      pos[k * 3] = u - 0.5;
      pos[k * 3 + 1] = (v - 0.5) / asp;
      pos[k * 3 + 2] = depth * hw * Math.sqrt(Math.max(0, 1 - nx * nx));
      uv[k * 2] = u; uv[k * 2 + 1] = v;
    }
  }
  for (let j = 0; j < rows; j++) for (let i = 0; i < cols; i++) {
    const a = j * (cols + 1) + i, b = a + 1, c = a + cols + 1, d = c + 1;
    idx.push(a, b, c, b, d, c);
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  g.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
  g.setIndex(idx);
  g.computeVertexNormals();
  return g;
}

const VERT = `
  varying vec2 vUv; varying vec3 vN; varying vec3 vP;
  void main() {
    vUv = uv;
    vec4 wp = modelMatrix * vec4(position, 1.0);
    vP = wp.xyz;
    vN = normalize(mat3(modelMatrix) * normal);
    gl_Position = projectionMatrix * viewMatrix * wp;
  }`;
const FRAG = `
  uniform sampler2D uMap; uniform vec3 uLight; uniform float uOpacity; uniform float uTint;
  varying vec2 vUv; varying vec3 vN; varying vec3 vP;
  void main() {
    vec4 c = texture2D(uMap, vUv);
    if (c.a < 0.04) discard;
    vec3 N = normalize(vN);
    vec3 V = normalize(cameraPosition - vP);
    vec3 Lv = normalize(uLight - vP);
    float ndl = max(dot(N, Lv), 0.0);
    float ndv = max(dot(N, V), 0.0);
    float sat = max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b));
    float gloss = mix(0.35, 1.0, smoothstep(0.1, 0.5, sat));
    // form shading: keeps brand colors, darkens gently toward the silhouette
    float shade = (0.72 + 0.34 * ndl) * mix(0.6, 1.0, pow(ndv, 0.6));
    vec3 col = c.rgb * shade;
    // controlled gloss: small specular + a soft window reflection
    vec3 H = normalize(Lv + V);
    float spec = pow(max(dot(N, H), 0.0), 90.0) * 0.22 * gloss;
    vec3 r = reflect(-V, N);
    float stripe = smoothstep(0.16, 0.0, abs(r.x - (uLight.x * 0.08 - 0.42))) * smoothstep(-0.2, 0.5, r.y) * 0.10 * gloss;
    col += vec3(1.0, 0.95, 0.9) * (spec + stripe);
    col *= uTint;
    gl_FragColor = vec4(col, c.a * uOpacity);
    #include <colorspace_fragment>
  }`;

export function initHero3D({ hero, card }) {
  const srcImg = card.querySelector('.hero-product');
  if (!srcImg || card.querySelector('.hero-gl')) return;

  const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const lite = matchMedia('(hover: none), (pointer: coarse)').matches; // phones/tablets: no pointer tracking

  let renderer;
  try {
    renderer = new THREE.WebGLRenderer({ alpha: true, antialias: !lite, powerPreference: 'high-performance' });
  } catch (e) { return; } // no WebGL -> the 2D hero stays as is

  const canvas = renderer.domElement;
  canvas.className = 'hero-gl';
  canvas.setAttribute('aria-hidden', 'true');
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, lite ? 1.5 : 2));
  renderer.setClearColor(0x000000, 0);
  card.insertBefore(canvas, card.firstChild);

  const disposables = [];
  let raf = 0, dead = false, visible = false, started = false, introStart = null;

  function fallback() {
    dead = true;
    cancelAnimationFrame(raf);
    card.classList.remove('gl-ready');
    canvas.remove();
    disposables.forEach(d => d());
    renderer.dispose();
  }
  canvas.addEventListener('webglcontextlost', e => { e.preventDefault(); fallback(); });

  new THREE.TextureLoader().load(
    srcImg.currentSrc || srcImg.src,
    tex => { try { build(tex); } catch (err) { console.warn('[hero3d]', err); fallback(); } },
    undefined,
    fallback
  );

  function build(tex) {
    tex.colorSpace = THREE.SRGBColorSpace;
    tex.anisotropy = Math.min(8, renderer.capabilities.getMaxAnisotropy());
    const img = tex.image;
    const asp = (img.naturalWidth || img.width) / (img.naturalHeight || img.height);

    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(34, 1, 0.1, 50);
    const R = 7;
    const tanHalf = Math.tan(THREE.MathUtils.degToRad(camera.fov / 2));

    // Light pool behind the pack
    const glowTex = radialTexture([[0, 'rgba(255,255,255,.9)'], [.45, 'rgba(255,255,255,.25)'], [1, 'rgba(255,255,255,0)']]);
    const glow = new THREE.Mesh(
      new THREE.PlaneGeometry(1, 1),
      new THREE.MeshBasicMaterial({ map: glowTex, transparent: true, opacity: 0.14, depthWrite: false, blending: THREE.AdditiveBlending })
    );
    glow.position.z = -1.4;
    scene.add(glow);

    // Contact shadow
    const shadowTex = radialTexture([[0, 'rgba(0,0,0,.55)'], [.6, 'rgba(0,0,0,.18)'], [1, 'rgba(0,0,0,0)']]);
    const shadow = new THREE.Mesh(
      new THREE.PlaneGeometry(1, 1),
      new THREE.MeshBasicMaterial({ map: shadowTex, transparent: true, depthWrite: false })
    );
    shadow.position.z = -0.05;
    scene.add(shadow);

    // The pack: inflated front shell + darker mirrored back shell (closes the volume when orbiting)
    const rows = lite ? 110 : 170, cols = lite ? 56 : 96;
    const sil = readSilhouette(img, asp, rows);
    const geo = inflateGeometry(smooth(sil.L), smooth(sil.R), asp, cols, rows, 0.75);
    const U = {
      uMap: { value: tex },
      uLight: { value: new THREE.Vector3(-1, 1, 3.2) },
      uOpacity: { value: 0 }
    };
    const mk = (tint) => new THREE.ShaderMaterial({
      transparent: true, depthWrite: true, depthTest: true,
      uniforms: { ...U, uTint: { value: tint } },
      vertexShader: VERT, fragmentShader: FRAG
    });
    const frontMat = mk(1.0), backMat = mk(0.55);
    const front = new THREE.Mesh(geo, frontMat);
    const back = new THREE.Mesh(geo, backMat);
    back.scale.z = -1; // mirrored; three.js flips the winding automatically
    front.renderOrder = 2; back.renderOrder = 1;
    const bottle = new THREE.Group();
    bottle.add(back, front);
    scene.add(bottle);

    // Dust particles
    const count = lite ? 28 : 64;
    const pos = new Float32Array(count * 3);
    for (let i = 0; i < count; i++) {
      pos[i * 3] = (Math.random() - 0.5) * 6;
      pos[i * 3 + 1] = (Math.random() - 0.5) * 5;
      pos[i * 3 + 2] = -2 + Math.random() * 4;
    }
    const pGeo = new THREE.BufferGeometry();
    pGeo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    const dotTex = radialTexture([[0, 'rgba(255,255,255,1)'], [.5, 'rgba(255,255,255,.5)'], [1, 'rgba(255,255,255,0)']]);
    const pMat = new THREE.PointsMaterial({ map: dotTex, size: 0.09, transparent: true, opacity: 0.55, depthWrite: false, color: 0xfff3df });
    const particles = new THREE.Points(pGeo, pMat);
    particles.renderOrder = 3;
    scene.add(particles);

    disposables.push(() => {
      [tex, glowTex, shadowTex, dotTex].forEach(t => t.dispose());
      [pGeo, glow.geometry, shadow.geometry, geo].forEach(g => g.dispose());
      [pMat, frontMat, backMat, glow.material, shadow.material].forEach(m => m.dispose());
    });

    // ---------- Layout (responsive to the card size) ----------
    const L = { halfH: 1, halfW: 1, pW: 1, pH: 1, baseY: 0 };
    function layout() {
      const w = card.clientWidth, h = card.clientHeight;
      if (!w || !h) return;
      renderer.setSize(w, h, false);
      camera.aspect = w / h;
      camera.updateProjectionMatrix();
      L.halfH = tanHalf * R;
      L.halfW = L.halfH * camera.aspect;
      L.pH = Math.min(L.halfH * 2 * 0.78, (L.halfW * 2 * 0.62) / asp);
      L.pW = L.pH * asp;
      L.baseY = -L.halfH + L.pH / 2 + L.halfH * 0.06;
      glow.scale.set(L.halfW * 3.4, L.halfW * 3.4, 1);
      shadow.scale.set(L.pW * 1.1, L.pH * 0.12, 1);
      shadow.position.y = L.baseY - L.pH / 2 + L.pH * 0.015;
    }

    // ---------- Input (mouse only; phones get an idle sway instead) ----------
    let tmx = 0, tmy = 0, mx = 0, my = 0, active = false;
    let lx = -0.4, ly = 0.4;
    let dragX = 0, dragY = 0, dX = 0, dY = 0, dragging = false, sx = 0, sy = 0;
    let scrollT = 0, sp = 0;

    const onScroll = () => { scrollT = clamp01(window.scrollY / (hero.offsetHeight * 0.85)); };
    onScroll();

    if (!lite && !reduce) {
      hero.addEventListener('pointermove', e => {
        if (e.pointerType !== 'mouse') return;
        const r = hero.getBoundingClientRect();
        tmx = clamp(((e.clientX - r.left) / r.width - 0.5) * 2, 1);
        tmy = clamp(((e.clientY - r.top) / r.height - 0.5) * 2, 1);
        active = true;
      });
      hero.addEventListener('pointerleave', () => { active = false; tmx = 0; tmy = 0; });
      canvas.addEventListener('pointerdown', e => {
        if (e.pointerType !== 'mouse' || e.button !== 0) return;
        dragging = true; sx = e.clientX; sy = e.clientY;
        canvas.setPointerCapture(e.pointerId);
      });
      canvas.addEventListener('pointermove', e => {
        if (!dragging) return;
        dragX = clamp((e.clientX - sx) / card.clientWidth * 1.2, 0.5);
        dragY = clamp((e.clientY - sy) / card.clientHeight * 0.5, 0.18);
      });
      const endDrag = () => { dragging = false; dragX = 0; dragY = 0; };
      canvas.addEventListener('pointerup', endDrag);
      canvas.addEventListener('pointercancel', endDrag);
    }
    if (!reduce) window.addEventListener('scroll', onScroll, { passive: true });

    // ---------- Render ----------
    let t = 0, last = 0;

    function draw(ip) {
      // camera orbit (yaw/pitch), dolly in on intro, pull back on scroll exit
      const sway = lite && !reduce ? Math.sin(t * 0.5) * 0.35 : 0;
      const yaw = (mx + sway) * 0.26 + dX + sp * 0.32;
      const pitch = -my * 0.12 + dY + (lite && !reduce ? Math.sin(t * 0.4) * 0.04 : 0);
      const radius = R + (1 - ip) * 2.8 + sp * 2.2;
      const cp = Math.cos(pitch);
      camera.position.set(Math.sin(yaw) * radius * cp, Math.sin(pitch) * radius, Math.cos(yaw) * radius * cp);
      camera.lookAt(0, 0, 0);

      // pack: gentle float + intro scale-in
      const float = reduce ? 0 : Math.sin(t * 1.1) * L.halfH * 0.012;
      bottle.scale.setScalar(L.pW * (0.9 + 0.1 * ip));
      bottle.position.y = L.baseY + float - (1 - ip) * L.halfH * 0.12;
      U.uOpacity.value = clamp01(ip * 1.6) * (1 - sp * 0.35);

      // light follows the cursor (or drifts slowly when idle)
      const tlx = active ? tmx : -0.4 + (reduce ? 0 : Math.sin(t * 0.4) * 0.25);
      const tly = active ? tmy : -0.35 + (reduce ? 0 : Math.cos(t * 0.33) * 0.15);
      lx += (tlx - lx) * 0.08; ly += (tly - ly) * 0.08;
      const wx = lx * L.halfW * 0.9, wy = -ly * L.halfH * 0.9;
      U.uLight.value.set(wx, wy, 3.2);
      glow.position.x = wx * 0.8; glow.position.y = wy * 0.8;

      particles.rotation.y = t * 0.02;
      particles.position.y = Math.sin(t * 0.3) * 0.08;
      pMat.opacity = 0.55 * ip * (1 - sp);

      renderer.render(scene, camera);
    }

    function frame(now) {
      if (dead || !visible) { raf = 0; return; }
      raf = requestAnimationFrame(frame);
      if (introStart === null) introStart = now;
      const dt = Math.min(0.05, last ? (now - last) / 1000 : 0.016);
      last = now;
      t += dt;
      const k = 1 - Math.exp(-dt * 5);
      mx += (tmx - mx) * k; my += (tmy - my) * k;
      dX += (dragX - dX) * k * 1.4; dY += (dragY - dY) * k * 1.4;
      sp += (scrollT - sp) * k;
      const ip = reduce ? 1 : easeOut(clamp01((now - introStart) / 1900));
      draw(ip);
      if (!started) { started = true; requestAnimationFrame(() => card.classList.add('gl-ready')); }
    }

    function run() {
      if (reduce) { layout(); draw(1); if (!started) { started = true; card.classList.add('gl-ready'); } return; }
      if (!raf && visible && !dead) { last = 0; raf = requestAnimationFrame(frame); }
    }

    layout();
    new ResizeObserver(() => { if (dead) return; layout(); if (reduce) draw(1); }).observe(card);
    new IntersectionObserver(([entry]) => { visible = entry.isIntersecting; if (visible) run(); }, { threshold: 0 }).observe(hero);
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) { cancelAnimationFrame(raf); raf = 0; } else run();
    });
    if (reduce) { visible = true; run(); }
  }
}
