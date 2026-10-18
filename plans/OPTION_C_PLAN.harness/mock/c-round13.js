
/* Round 13 (owner, 2026-10-18): in History's pass-rate chart a run that failed is told by more than its colour (WCAG
   1.4.1, Use of Color): its bar is hatched. The bars stand the same height whether a run failed or not (its pass rate
   rounds to 99 or 100%), so only red against green said which runs failed, and under protanopia or deuteranopia the
   two read as one olive. The stripes are the card's colour over the fail colour, so they hold in both themes. */
(function () {
  var doc = document, NS = 'http://www.w3.org/2000/svg', n = 0;
  function hatch(svg) {
    var fails = svg.querySelectorAll('.history-bar-fail');
    if (!fails.length || svg.querySelector('pattern.k-fail-hatch')) return;
    var defs = svg.querySelector('defs');
    if (!defs) { defs = doc.createElementNS(NS, 'defs'); svg.insertBefore(defs, svg.firstChild); }
    var id = 'k-fail-hatch-' + (++n);
    var p = doc.createElementNS(NS, 'pattern');
    p.setAttribute('class', 'k-fail-hatch');
    p.setAttribute('id', id);
    p.setAttribute('patternUnits', 'userSpaceOnUse');
    p.setAttribute('width', '6');
    p.setAttribute('height', '6');
    // the chart stretches its drawing to its box: the pattern undoes the stretch, so the stripes lie at 45 degrees
    // and 6 px apart on screen
    // how much one of the chart's own units is on screen, read from a bar: its box in the chart's units against its
    // box as drawn (the chart stretches its drawing to its box). History draws the chart before its tab shows, when
    // the bar has no size yet, so this is read again whenever the chart is laid out or resized.
    var fit = function () {
      var bb = fails[0].getBBox(), r = fails[0].getBoundingClientRect();
      if (!bb.width || !r.width) return;
      p.setAttribute('patternTransform', 'scale(' + (bb.width / r.width) + ' ' + (bb.height / r.height) + ') rotate(45)');
    };
    p.setAttribute('patternTransform', 'rotate(45)');
    fit();
    if (window.ResizeObserver) new ResizeObserver(fit).observe(svg);
    var base = doc.createElementNS(NS, 'rect');
    base.setAttribute('width', '6'); base.setAttribute('height', '6');
    base.style.fill = 'var(--kron-fail)';
    var stripe = doc.createElementNS(NS, 'rect');
    stripe.setAttribute('width', '2'); stripe.setAttribute('height', '6');
    stripe.style.fill = 'var(--kron-card)'; stripe.style.opacity = '0.45';
    p.appendChild(base); p.appendChild(stripe); defs.appendChild(p);
    Array.prototype.forEach.call(fails, function (b) { b.style.fill = 'url(#' + id + ')'; });
  }
  function scan(root) {
    if (!root || !root.querySelectorAll) return;
    if (root.matches && root.matches('svg.history-chart-svg')) hatch(root);
    Array.prototype.forEach.call(root.querySelectorAll('svg.history-chart-svg'), hatch);
  }
  function run() {
    scan(doc);
    // History draws its charts when it is first opened
    new MutationObserver(function (records) {
      records.forEach(function (r) { Array.prototype.forEach.call(r.addedNodes, function (a) { if (a.nodeType === 1) scan(a); }); });
    }).observe(doc.body, { childList: true, subtree: true });
  }
  if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', function () { setTimeout(run, 0); });
  else setTimeout(run, 0);
})();

/* Round 13: a control reached by Tab or Shift+Tab is never left under the pinned bar (WCAG 2.2 2.4.11, Focus Not
   Obscured). The page's scroll padding (c-contrast.css) covers every scroll the browser makes, but Firefox makes none
   for a control that already shows in part, so a feature heading reached by Shift+Tab stayed half under the bar.
   Once focus has moved, a control the bar covers is brought just below it. Firefox also leaves a link inside a
   diagram (an arrow's label) where it is when Tab reaches it below the window (WCAG 2.4.7, Focus Visible): it is
   brought up into the window. A control in the bar itself, or in a popup or a sheet that floats over the page, is
   left where it is. */
(function () {
  var doc = document;
  function floats(t) {
    for (var n = t; n && n.nodeType === 1; n = n.parentElement) if (getComputedStyle(n).position === 'fixed') return true;
    return false;
  }
  function clear(t, nav) {
    if (doc.activeElement !== t || floats(t)) return;
    var pos = getComputedStyle(nav).position;
    if (pos !== 'sticky' && pos !== 'fixed') return;
    var r = t.getBoundingClientRect();
    if (!r.height) return;
    var top = nav.getBoundingClientRect().bottom + 12, bottom = window.innerHeight - 12;
    // round 17 (owner): a control taller than the free window that already shows in it is not obscured; bringing its
    // top down moved a whole scenario (item 20)
    if (r.height > bottom - top && r.bottom > top + 24 && r.top < bottom - 24) return;
    if (r.top < top) window.scrollBy(0, r.top - top);
    else if (r.bottom > bottom) window.scrollBy(0, Math.min(r.bottom - bottom, r.top - top));
  }
  // round 17 (owner, item 20): focus a click gives (the scenario a diagram sits in, say) is on screen where the reader
  // pointed; only focus from the keyboard is moved. Clicking in and out of a diagram scrolled the page in Firefox.
  var pointerAt = 0;
  doc.addEventListener('pointerdown', function () { pointerAt = Date.now(); }, true);
  doc.addEventListener('keydown', function () { pointerAt = 0; }, true);
  doc.addEventListener('focusin', function (e) {
    var t = e.target, nav = doc.querySelector('.toolbar-row.report-nav');
    if (!nav || !t || !t.getBoundingClientRect || nav.contains(t)) return;
    if (Date.now() - pointerAt < 1000) return;
    requestAnimationFrame(function () { clear(t, nav); });
    // the page can still move under the control once focus has landed: a diagram above it finishes drawing and
    // changes height, and Firefox, which does not anchor the page on the focused control as Chrome does, lets the
    // control slide under the bar. For a second after focus moves, every scroll the page makes is checked; the
    // reader scrolling or moving focus on ends the watch.
    var timers = [];
    var watch = function () { clear(t, nav); };
    var stop = function () {
      window.removeEventListener('scroll', watch);
      window.removeEventListener('wheel', stop);
      window.removeEventListener('touchstart', stop);
      t.removeEventListener('focusout', stop);
      timers.forEach(clearTimeout);
    };
    window.addEventListener('scroll', watch, { passive: true });
    window.addEventListener('wheel', stop, { passive: true });
    window.addEventListener('touchstart', stop, { passive: true });
    t.addEventListener('focusout', stop);
    [150, 400, 800].forEach(function (ms) { timers.push(setTimeout(watch, ms)); });
    timers.push(setTimeout(stop, 1000));
  }, true);
})();

/* Round 13: the deep search's status chip sits inside the search field, at its right end. Today's report puts it 3em
   from the end of the search line, which held only the field and the help button; in C the line also holds More
   filters (round 9), so the chip landed on the two buttons and hid their labels. It is placed from the field's own
   edge whenever it is shown, and when the window changes size. */
(function () {
  var doc = document;
  function place(chip) {
    var box = chip.parentElement, field = box && box.querySelector('input');
    if (!field || chip.style.display === 'none') return;
    var b = box.getBoundingClientRect(), f = field.getBoundingClientRect();
    chip.style.right = Math.max(0, Math.round(b.right - f.right) + 8) + 'px';
  }
  function watch(chip) {
    if (chip.__kPlaced) return;
    chip.__kPlaced = true;
    new MutationObserver(function () { place(chip); }).observe(chip, { attributes: true, attributeFilter: ['style'], childList: true, characterData: true, subtree: true });
    window.addEventListener('resize', function () { place(chip); });
    place(chip);
  }
  function run() {
    var box = doc.querySelector('.filter-search');
    if (!box) return;
    var found = box.querySelector('.kron-deep-chip');
    if (found) watch(found);
    // the search script adds the chip the first time it has something to say
    new MutationObserver(function () { var c = box.querySelector('.kron-deep-chip'); if (c) watch(c); }).observe(box, { childList: true });
  }
  if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', function () { setTimeout(run, 0); });
  else setTimeout(run, 0);
})();
