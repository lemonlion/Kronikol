
/* Round 8 (owner's notes, 2026-10-17, fifth batch; reworked on the owner's next note): the report bar carries the
   filters. Right of the tabs, on every tab: what the filters leave showing, Clear All while anything is filtered,
   and a Filters button that brings the filter box into the bar, under its rows. There it is part of the bar, not a
   layer over the page: it pins with the bar, covers nothing, and leaves no gap where it was. While it is in the bar
   the list below keeps at least a window's height, so filtering down to a few scenarios, or none, cannot pull the
   page back to the top: the bar stays put and the results start right under it. Opening and closing keep what you
   were reading where it was, just below the bar. Desktop only: on a phone the bar scrolls away and the box has its
   own Filters toggle. */
(function () {
  var doc = document, root = doc.documentElement;
  function $(s, r) { return (r || doc).querySelector(s); }
  function $$(s, r) { return Array.prototype.slice.call((r || doc).querySelectorAll(s)); }
  function el(tag, cls, text) { var e = doc.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; }
  var phone = window.matchMedia ? window.matchMedia('(max-width: 768px)') : { matches: false };

  function run() {
    var nav = $('.toolbar-row.report-nav'), tabs = nav && $('.report-tabs', nav), box = $('.filtering-box');
    var readout = box && $('.filter-readout', box);
    var boxClear = box && $$('.filtering-box-export .export-btn', box).filter(function (b) { return /clear all/i.test(b.textContent); })[0];
    if (!nav || !tabs || !box || !readout) return;
    var home = { parent: box.parentNode, next: box.nextSibling };
    if (!box.id) box.id = 'filtering-box';

    // the tabs and the filters share one row; the tabs scroll inside it if they must, the filters never do
    var row = el('div', 'nav-row');
    tabs.parentNode.insertBefore(row, tabs);
    row.appendChild(tabs);
    var group = el('div', 'nav-filters');
    group.setAttribute('role', 'group');
    group.setAttribute('aria-label', 'Filters');
    // the box's readout already announces changes (role=status): the copy here stays quiet
    var text = el('span', 'nav-readout');
    text.setAttribute('aria-hidden', 'true');
    var long = el('span', 'nav-readout-long'), short = el('span', 'nav-readout-short');
    text.appendChild(long);
    text.appendChild(short);
    var clear = el('button', 'nav-clear', 'Clear All');
    clear.type = 'button';
    clear.title = 'Clear every filter';
    clear.addEventListener('click', function () { if (boxClear) boxClear.click(); });
    var toggle = el('button', 'nav-filters-toggle');
    toggle.type = 'button';
    toggle.setAttribute('aria-expanded', 'false');
    toggle.setAttribute('aria-controls', box.id);
    toggle.title = 'Show the filter box in this bar, so it stays in view as you scroll';
    toggle.innerHTML = '<svg viewBox="0 0 16 16" width="14" height="14" aria-hidden="true" focusable="false"><path d="M2.5 3h11l-4.25 5v4.25l-2.5 1.25V8z" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round"/></svg><span>Filters</span>';
    group.appendChild(text);
    group.appendChild(clear);
    group.appendChild(toggle);
    row.appendChild(group);

    // nothing left to show: say so where the list was, with the way back
    var featuresPanel = $('.report-panel[data-panel="features"]');
    var empty = el('div', 'filters-empty');
    empty.hidden = true;
    empty.appendChild(el('span', null, 'No scenarios match the filters.'));
    var emptyClear = el('button', 'filters-empty-clear', 'Clear All');
    emptyClear.type = 'button';
    emptyClear.addEventListener('click', function () {
      if (boxClear) boxClear.click();
      // round 16: the empty state goes with the filters it reported, and focus with it, to nothing (the page); it is
      // given to the search field, where a reader starts again (WCAG 2.4.3)
      var search = document.getElementById('searchbar');
      if (search && search.getClientRects().length) search.focus({ preventScroll: true });
    });
    empty.appendChild(emptyClear);
    if (featuresPanel) featuresPanel.insertBefore(empty, featuresPanel.firstChild);

    var riding = false;
    // the copy follows the box's readout: the whole line, and a short form for a narrow bar
    function sync() {
      var filtered = readout.classList.contains('is-filtered'), now = readout.textContent;
      long.innerHTML = readout.innerHTML;
      var m = /(\d+) of (\d+) scenarios/.exec(now), all = /All (\d+) scenarios/.exec(now);
      short.innerHTML = '';
      if (filtered && m) { short.appendChild(el('b', null, 'Filtered')); short.appendChild(doc.createTextNode(' ' + m[1] + ' of ' + m[2])); }
      else short.textContent = all ? 'All ' + all[1] + ' scenarios' : now;
      text.classList.toggle('is-filtered', filtered);
      toggle.classList.toggle('is-active', filtered);
      clear.hidden = !filtered;
      empty.hidden = !(filtered && m && m[1] === '0');
      fit();
      settle();
    }
    new MutationObserver(sync).observe(readout, { childList: true, characterData: true, subtree: true, attributes: true, attributeFilter: ['class'] });

    // a narrow bar drops the readout to its short form, then drops it, before the tabs would have to scroll
    function fit() {
      row.classList.remove('nav-compact', 'nav-minimal');
      if (phone.matches) return;
      var room = function () { return tabs.scrollWidth + group.offsetWidth + 24 <= row.clientWidth; };
      if (room()) return;
      row.classList.add('nav-compact');
      if (room()) return;
      row.classList.add('nav-minimal');
    }

    // where the bar starts in the page: scrolled that far, it is pinned and the list starts right under it
    var mark = el('div', 'nav-mark');
    nav.parentNode.insertBefore(mark, nav);
    if (home.next === nav) home.next = mark;
    function pinAt() { return mark.getBoundingClientRect().top + window.pageYOffset - (parseFloat(getComputedStyle(nav).top) || 0); }

    // When a filter changes the list, Chrome keeps the page anchored on the focused control; with that control in
    // the pinned bar it scrolls the page to the top. So where the page was when a filter control was used decides,
    // before anything is painted: at or past the list's start, the results from the first one right under the bar;
    // above it, where you were, while the first results are in sight. Only for a filter used from the bar, or with
    // the box in it; with the box in its own place the browser's handling is left as it was.
    var before = null, shown = readout.textContent;
    function note(e) { if (!e.target.closest('.nav-filters-toggle, .filters-close')) before = { y: window.pageYOffset, at: Date.now() }; }
    [nav, box, empty].forEach(function (n) { ['pointerdown', 'keydown', 'input'].forEach(function (t) { n.addEventListener(t, note, true); }); });
    // a search answers twice (the quick pass, then the deep one), so the position holds for both, until you scroll
    function forget() { before = null; }
    window.addEventListener('wheel', forget, { passive: true });
    window.addEventListener('touchmove', forget, { passive: true });
    doc.addEventListener('keydown', function (e) {
      if (!nav.contains(e.target) && !box.contains(e.target) && /^(PageUp|PageDown|Home|End|ArrowUp|ArrowDown| )$/.test(e.key)) forget();
    });
    function settle() {
      // the readout is rewritten whenever anything in the list changes style: only a new result counts
      var now = readout.textContent, changed = now !== shown;
      shown = now;
      if (!changed) return;
      var was = before && Date.now() - before.at < 3000 ? before.y : null;
      if (was === null) return;
      var y = pinAt();
      if (!riding && was < y - 1) return;
      if (was >= y - 1) { window.scrollTo(0, y); return; }
      // above the list's start: where you were, unless that leaves the first results out of sight below the bar
      window.scrollTo(0, was);
      if (nav.getBoundingClientRect().bottom + 14 > window.innerHeight - 120) window.scrollTo(0, y);
    }

    // what you were reading just below a pinned bar stays just below it when the bar changes height
    function reading() {
      if (window.pageYOffset < pinAt() - 0.5) return null;
      var r = nav.getBoundingClientRect();
      var e = doc.elementFromPoint(Math.round(r.left + r.width / 2), Math.round(r.bottom + 8));
      if (!e || e === root || e === doc.body || nav.contains(e)) return null;
      return { e: e, off: e.getBoundingClientRect().top - r.bottom };
    }
    function keep(k) {
      if (!k || !k.e.isConnected) return;
      var d = k.e.getBoundingClientRect().top - nav.getBoundingClientRect().bottom - k.off;
      if (Math.abs(d) > 0.5) window.scrollBy(0, d);
    }
    // the browser's own scroll anchoring would move the page as well: off while the box moves
    function moving(fn) {
      root.style.overflowAnchor = 'none';
      try { fn(); } finally { requestAnimationFrame(function () { root.style.overflowAnchor = ''; }); }
    }

    // in the bar, the box takes at most what leaves the bar 60% of the window, and scrolls inside past that
    function capBox() {
      box.style.maxHeight = '';
      if (!riding) return;
      var rows = nav.getBoundingClientRect().height - box.getBoundingClientRect().height;
      var cap = Math.max(180, Math.round(window.innerHeight * 0.6 - rows));
      if (box.scrollHeight > cap + 1) box.style.maxHeight = cap + 'px';
    }

    var closeBtn = el('button', 'filters-close');
    closeBtn.type = 'button';
    closeBtn.setAttribute('aria-label', 'Take the filters out of the bar');
    closeBtn.title = 'Put the filter box back above the bar (Esc)';
    closeBtn.innerHTML = '<svg viewBox="0 0 16 16" width="14" height="14" aria-hidden="true" focusable="false"><path d="M4 4l8 8M12 4l-8 8" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/></svg>';

    function open() {
      if (riding || phone.matches) return;
      var k = reading();
      moving(function () {
        // round 17 (owner, item 11): at the top of the bar, where the box sits above the bar at rest
        nav.insertBefore(box, nav.firstChild);
        riding = true;
        nav.classList.add('has-filters');
        group.classList.add('riding');
        ($('.filtering-box-export', box) || box).appendChild(closeBtn);
        toggle.setAttribute('aria-expanded', 'true');
        toggle.title = 'Put the filter box back above the bar';
        // the box's rows are measured on its width, and the bar's height feeds the anchored scrolls
        window.dispatchEvent(new Event('resize'));
        capBox();
        keep(k);
        // opened near the top with the bar low in a short window: scroll until the box is in view, or the bar pinned
        if (!k) {
          var stick = parseFloat(getComputedStyle(nav).top) || 0, nr = nav.getBoundingClientRect();
          var need = box.getBoundingClientRect().bottom + 8 - window.innerHeight;
          if (need > 0 && nr.top > stick) window.scrollBy(0, Math.min(need, nr.top - stick));
        }
      });
      var first = $('#searchbar', box) || $('input, button', box);
      if (first) first.focus({ preventScroll: true });
    }
    function shut(refocus) {
      if (!riding) return;
      var k = reading();
      moving(function () {
        home.parent.insertBefore(box, home.next);
        riding = false;
        box.style.maxHeight = '';
        nav.classList.remove('has-filters');
        group.classList.remove('riding');
        if (closeBtn.parentNode) closeBtn.parentNode.removeChild(closeBtn);
        toggle.setAttribute('aria-expanded', 'false');
        toggle.title = 'Show the filter box in this bar, so it stays in view as you scroll';
        window.dispatchEvent(new Event('resize'));
        keep(k);
      });
      if (refocus) toggle.focus({ preventScroll: true });
    }
    toggle.addEventListener('click', function () { if (riding) shut(false); else open(); });
    closeBtn.addEventListener('click', function () { shut(true); });
    function onEscape(e) { if (e.key === 'Escape' && riding) { e.preventDefault(); shut(true); } }
    box.addEventListener('keydown', onEscape);
    group.addEventListener('keydown', onEscape);
    window.addEventListener('resize', function () { if (phone.matches) shut(false); fit(); capBox(); });
    $$('.report-tab', nav).forEach(function (t) { t.addEventListener('click', function () { setTimeout(fit, 0); }); });
    // the box changing its own height (round 9 folds its rows): in the bar, what is below the bar stays put
    function reflow(fn) {
      if (!riding) { fn(); window.dispatchEvent(new Event('resize')); return; }
      var k = reading();
      moving(function () { fn(); window.dispatchEvent(new Event('resize')); capBox(); keep(k); });
    }
    sync();
    // Round 17 (owner, item 11): the Filters button shows only while the bar floats; at rest the box is in view above
    // the bar. Once the bar comes to rest, a box riding in it goes back above it.
    var sentinel = el('div', 'nav-float-sentinel');
    sentinel.setAttribute('aria-hidden', 'true');
    nav.parentNode.insertBefore(sentinel, nav);
    var floating = null;
    function checkFloat() {
      var stick = parseFloat(getComputedStyle(nav).top) || 0;
      // at the pin line counts as floating: after a filter change the results are scrolled to start right there
      var f = !phone.matches && sentinel.getBoundingClientRect().top <= stick + 0.5;
      if (f === floating) return;
      floating = f;
      nav.classList.toggle('k-floating', f);
      if (!f && riding) shut(doc.activeElement && box.contains(doc.activeElement));
    }
    var ticking = false;
    window.addEventListener('scroll', function () {
      if (ticking) return;
      ticking = true;
      requestAnimationFrame(function () { ticking = false; checkFloat(); });
    }, { passive: true });
    window.addEventListener('resize', checkFloat);
    checkFloat();
    window.__kronikolNavFilters = { open: open, close: shut, reflow: reflow };
  }
  if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', function () { setTimeout(run, 0); });
  else setTimeout(run, 0);
})();
