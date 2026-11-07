// PROTOTYPE of TOGGLE_JUMP_PLAN.md section 4 (P1 to P5, P7), injected into the page by jump.js with PROTO=1. It is
// built on what the report's scripts export, as a probe cannot change the scripts themselves:
// - both swaps call window._zoomState.give right after the new picture is written, before anything is laid out (the
//   whole picture's swapPicture, the parts' zoomAfter), so the hold (P3) and the pre-sizing (P5) go there, and keep
//   (P2) is queued as a microtask, which runs once the swap's task has given the zoom back (afterRedraw), before the
//   paint;
// - the parts swap calls window._sizeHiddenParts once the old parts are gone, before it shows the new ones: the hold
//   starts there for parts;
// - take (P1) is a capture listener on the controls, so it reads the place before any control's own handler writes.
// P6 is not prototyped: with the picture sized at the swap, the restore's resizes() finds nothing to change, and so
// keeps no place (the log shows whether any scrollBy still comes from keepPlace).
// PROTO_OFF=take,keep,hold,p5,let (comma-separated) turns pieces off, for the mutations.
(() => {
  const off = new Set(String(window.__protoOff || '').split(',').filter(Boolean));
  const log = (window.__protoLog = []);
  const t0 = performance.now();
  const note = (what, extra) => log.push(Object.assign({ t: Math.round(performance.now() - t0), what, y: Math.round(window.scrollY) }, extra || {}));
  let place = null, held = 0, carry = 0, writing = false;

  const boxOf = (el) => (el && el.closest ? el.closest('[data-diagram-type="plantuml"]') : null);
  // the pictures a box shows: its drawn parts' (not the hidden new ones), else its own
  const picturesOf = (box) => {
    const parts = Array.from(box.querySelectorAll(':scope > .puml-fragment:not(.puml-fragment-new)'))
      .map((p) => p.querySelector(':scope > svg')).filter(Boolean);
    if (parts.length) return parts;
    const svg = box.querySelector(':scope > svg');
    return svg ? [svg] : [];
  };
  // from the first picture's top to the last one's bottom
  const span = (box) => {
    const pics = picturesOf(box);
    if (!pics.length) return null;
    const a = pics[0].getBoundingClientRect(), b = pics[pics.length - 1].getBoundingClientRect();
    return { top: a.top, height: b.bottom - a.top };
  };
  // the reading line (Q1 (a)): the middle of the window below the bar and any stuck settings bar
  const readingLine = () => {
    const below = document.elementFromPoint(window.innerWidth / 2, window.innerHeight / 2) || document.body;
    let top = 0;
    try { top = window.kronikol_freeTop ? window.kronikol_freeTop(below) : 0; } catch (e) { top = 0; }
    return (top + window.innerHeight) / 2;
  };
  const fixed = (el) => {
    for (let e = el; e && e !== document.body; e = e.parentElement) {
      const p = getComputedStyle(e).position;
      if (p === 'fixed' || p === 'sticky') return true;
    }
    return false;
  };

  function take() {
    if (place || off.has('take')) return;
    const y = readingLine(), x = window.innerWidth / 2;
    for (const el of document.elementsFromPoint(x, y)) {
      if (el === document.documentElement || el === document.body) continue;
      const box = boxOf(el);
      if (box) {
        const s = span(box);
        if (s && s.height > 0 && y >= s.top && y <= s.top + s.height) {
          place = { box, fy: (y - s.top) / s.height, y, h0: s.height };
          // PROTO_MODE=landmark: the drawn text nearest the line too, by its words and which time they occur in the box
          if (window.__protoMode === 'landmark') {
            const texts = Array.from(box.querySelectorAll('svg text'));
            let best = -1, bd = Infinity;
            texts.forEach((t, i) => { const q = t.getBoundingClientRect(); if (!q.height) return;
              const d = Math.abs((q.top + q.bottom) / 2 - y); if (d < bd) { bd = d; best = i; } });
            if (best >= 0) {
              const words = texts[best].textContent, q = texts[best].getBoundingClientRect();
              place.mark = { words, k: texts.slice(0, best).filter((t) => t.textContent === words).length, off: (q.top + q.bottom) / 2 - y };
            }
          }
          break;
        }
      }
      if (!fixed(el)) { place = { el, top: el.getBoundingClientRect().top }; break; }
    }
    carry = 0;
    note('take', place ? (place.box ? { fy: +place.fy.toFixed(4), line: Math.round(y) } : { el: place.el.tagName + '.' + place.el.className, top: Math.round(place.top) }) : { none: true });
    if (place) watchEnd(place);
  }

  function keep() {
    if (!place || off.has('keep')) return;
    let dy;
    if (place.box) {
      if (!place.box.isConnected) { place = null; return; }
      const s = span(place.box);
      if (!s || !(s.height > 0)) return;
      dy = s.top + place.fy * s.height - place.y;
      if (place.mark) {
        // the landmark found again, believed only within the box's own change of height of the fraction's answer
        const t0 = performance.now();
        const same = Array.from(place.box.querySelectorAll('svg text')).filter((t) => t.textContent === place.mark.words);
        const t = same[place.mark.k];
        if (t) {
          const q = t.getBoundingClientRect(), dl = (q.top + q.bottom) / 2 - (place.y + place.mark.off);
          if (Math.abs(dl - dy) <= Math.abs(s.height - place.h0) + 30) { note('landmark', { ms: +(performance.now() - t0).toFixed(2) }); dy = dl; }
          else note('landmark refused', { dl: Math.round(dl) });
        } else note('landmark lost');
      }
    } else {
      if (!place.el.isConnected) { place = null; return; }
      dy = place.el.getBoundingClientRect().top - place.top;
    }
    dy += carry;
    if (Math.abs(dy) <= 0.5) { carry = dy; return; }
    const was = window.scrollY;
    writing = true;
    window.scrollBy(0, dy);
    writing = false;
    // what the window could not scroll: a fraction of a pixel, or all of it at the page's end
    carry = Math.max(-1, Math.min(1, dy - (window.scrollY - was)));
    note('keep', { dy: Math.round(dy * 100) / 100 });
  }

  // P3: the browser's anchoring held off from the swap to the frame after it. On the body, not the root, so that the
  // zoom code's own hold, which clears the root's style when it ends, cannot end this one.
  function hold() {
    if (!place || off.has('hold')) return;
    if (!held++) document.body.style.overflowAnchor = 'none';
    requestAnimationFrame(() => setTimeout(() => { if (!--held) document.body.style.overflowAnchor = ''; }, 0));
  }

  // P5: a zoomed box's new whole picture sized at its level at once, from the width the engine wrote on it and the
  // content width the zoom code last measured (the plan reads it before the swap)
  function presize(box, state) {
    if (off.has('p5') || !(state && (state.zoomed || state.out))) return;
    if (box.querySelector(':scope > .puml-fragment')) return; // parts: _sizeHiddenParts sized them
    const svg = box.querySelector(':scope > svg');
    const w = svg && svg.width && svg.width.baseVal;
    if (!(w && (w.unitType === 1 || w.unitType === 5) && w.value > 0)) return;
    const natural = w.value, cw = box._kContentWidth;
    if (!(cw > 0)) return;
    const fitPct = Math.max(1, Math.min(100, Math.round(cw / natural * 100))), wide = fitPct < 100;
    const percent = Math.round(state.percent || 0);
    if (state.zoomed && wide) {
      const pct = Math.max(fitPct, Math.min(100, percent));
      if (pct > fitPct) { svg.style.maxWidth = 'none'; svg.style.width = pct >= 100 ? '' : (natural * pct / 100) + 'px'; }
    } else if (state.out && !wide && percent > 0 && percent < 100) {
      svg.style.maxWidth = '100%';
      svg.style.width = (natural * percent / 100) + 'px';
    }
  }

  const give = window._zoomState.give;
  window._zoomState.give = function (box, state) {
    hold();
    const out = give.apply(this, arguments);
    presize(box, state);
    if (place) queueMicrotask(keep);
    return out;
  };
  const sizeHidden = window._sizeHiddenParts;
  if (sizeHidden) window._sizeHiddenParts = function () { hold(); return sizeHidden.apply(this, arguments); };

  // P4 and the end: let go on the reader's own scroll, and once no diagram is queued or drawing for two frames
  const letGo = (why) => { if (place && !off.has('let')) { note('let go', { why }); place = null; } };
  window.addEventListener('wheel', () => letGo('wheel'), { capture: true, passive: true });
  window.addEventListener('touchstart', () => letGo('touch'), { capture: true, passive: true });
  window.addEventListener('keydown', (e) => {
    const k = e.key, field = e.target && e.target.closest && e.target.closest('input, textarea, select, [contenteditable]');
    if (!field && /^(PageUp|PageDown|Home|End|ArrowUp|ArrowDown| )$/.test(k)) letGo('key ' + k);
  }, true);
  window.addEventListener('mousedown', (e) => { if (e.target === document.documentElement) letGo('scrollbar'); }, true);
  function busy() {
    if (window._plantumlRendering) return true;
    for (const c of document.querySelectorAll('[data-diagram-type]')) if (c._kQueued > 0 || c._kBatched > 0 || c._noteRendering) return true;
    return false;
  }
  function watchEnd(mine) {
    let quiet = 0;
    const tick = () => {
      if (place !== mine) return;
      quiet = busy() ? 0 : quiet + 1;
      if (quiet >= 2) { note('end'); place = null; return; }
      requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
  }

  // P1: read before any control's own handler runs (P7: a second control while the first draws keeps the first's)
  document.addEventListener('click', (e) => {
    const b = e.target && e.target.closest && e.target.closest('button[data-toggle], .details-radio-btn');
    if (b) take();
  }, true);
  window.__proto = { take, keep, get place() { return place; } };
})();
