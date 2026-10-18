
/* Round 9 (owner's note, 2026-10-18): everything under the search field stays folded until you ask for it. A More
   filters button at the end of the search line opens the rows (status, duration, happy paths, tags, dependencies)
   and, while they are folded, counts the filters in them that are on. The header's readout and Clear All stay in
   view either way, and the search syntax help still opens under the search field. Phones keep the report's own
   Filters toggle, which already folds the rows there. */
(function () {
  var doc = document;
  function $(s, r) { return (r || doc).querySelector(s); }
  function el(tag, cls, text) { var e = doc.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; }

  function run() {
    var box = $('.filtering-box'), search = box && $('.filter-search', box), rows = box && $('.filters', box);
    if (!box || !search || !rows) return;
    if (!rows.id) rows.id = 'filter-rows';
    var help = $('.search-help-panel', rows);

    var more = el('button', 'more-filters');
    more.type = 'button';
    more.setAttribute('aria-controls', rows.id);
    var count = el('span', 'more-filters-count');
    more.appendChild(el('span', 'more-filters-label', 'More filters'));
    more.appendChild(count);
    more.insertAdjacentHTML('beforeend', '<svg viewBox="0 0 16 16" width="12" height="12" aria-hidden="true" focusable="false"><path d="M4 6l4 4 4-4" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>');
    search.appendChild(more);

    // the filters in the rows that are on: each status, tag or dependency chip set either way, a category, and the
    // duration threshold (one, whether a percentile or a custom value)
    function on() {
      var n = box.querySelectorAll('.status-toggle.status-active, .dependency-toggle.dependency-active, .dependency-toggle.tagf-out, .tag-toggle[data-tag-state="in"], .tag-toggle[data-tag-state="out"]').length;
      if (!$('.category-toggle.category-active[data-category=""]', box)) n += box.querySelectorAll('.category-toggle.category-active').length;
      var dur = $('#duration-threshold', box);
      if ($('.percentile-btn.percentile-active', box) || (dur && dur.value)) n += 1;
      return n;
    }
    function paint() {
      var open = !box.classList.contains('rows-folded'), n = on();
      more.setAttribute('aria-expanded', String(open));
      count.textContent = n ? String(n) : '';
      count.hidden = open || !n;
      more.classList.toggle('has-on', !open && n > 0);
      more.setAttribute('aria-label', !open && n ? 'More filters, ' + n + ' on' : 'More filters');
      more.title = open ? 'Fold the filters away' : n ? (n === 1 ? 'One filter here is on' : n + ' filters here are on') + ': show them' : 'Status, duration, happy paths, tags and dependencies';
    }
    function set(open) {
      var apply = function () { box.classList.toggle('rows-folded', !open); paint(); };
      // in the bar (round 8) the bar grows or shrinks with the rows, and what is below it stays put
      var nav = window.__kronikolNavFilters;
      if (nav && nav.reflow) nav.reflow(apply);
      else { apply(); window.dispatchEvent(new Event('resize')); }
    }
    box.classList.add('rows-folded');
    more.addEventListener('click', function () { set(box.classList.contains('rows-folded')); });

    // the search help lives in the rows' container: it opens on its own while the rows are folded
    function helpOpen() { box.classList.toggle('help-open', !!help && help.style.display !== 'none'); }
    if (help) { new MutationObserver(helpOpen).observe(help, { attributes: true, attributeFilter: ['style'] }); helpOpen(); }

    // the count follows every change: a control in the box, Clear All anywhere, a filter read from the address
    ['click', 'input', 'change', 'keyup'].forEach(function (t) { doc.addEventListener(t, function () { setTimeout(paint, 0); }, true); });
    var readout = $('.filter-readout', box);
    if (readout) new MutationObserver(paint).observe(readout, { childList: true, subtree: true, characterData: true });
    paint();
  }
  if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', function () { setTimeout(run, 0); });
  else setTimeout(run, 0);
})();

/* Round 9 (owner's notes, 2026-10-18): the tab bar and what its tab shows are one container, and the Components tab
   heads its diagram the way the Timeline heads its bars.
   - The bar is the container's top and the panel under it its body (c-round9.css). The bar casts its shadow, and
     squares its top corners, only while it is pinned over the list scrolling under it.
   - "Component Diagram" and an information mark open the component header, as "Scenario Timeline" opens the
     timeline's, with the controls on the same line. */
(function () {
  var doc = document;
  function $(s, r) { return (r || doc).querySelector(s); }
  function run() {
    var nav = $('.toolbar-row.report-nav'), mark = $('.nav-mark');
    if (nav && mark) {
      var sticky = false, stick = 0;
      var measure = function () { var cs = getComputedStyle(nav); sticky = cs.position === 'sticky'; stick = parseFloat(cs.top) || 0; };
      var pinned = function () { nav.classList.toggle('is-pinned', sticky && mark.getBoundingClientRect().top < stick - 0.5); };
      var queued = false;
      window.addEventListener('scroll', function () {
        if (queued) return;
        queued = true;
        requestAnimationFrame(function () { queued = false; pinned(); });
      }, { passive: true });
      window.addEventListener('resize', function () { measure(); pinned(); });
      measure();
      pinned();
    }
    var head = $('.component-header');
    if (head && !$('.timeline-info', head)) {
      var info = doc.createElement('span');
      info.className = 'timeline-info';
      info.title = 'The Component Diagram shows the services, databases and queues the scenarios called, and the calls between them. Each connection is labelled with the calls made along it and how many tests made them. Filtered draws only what the scenarios the filters show used; Full draws the whole run. Use it to see what a set of tests depends on.';
      info.textContent = '🛈';
      head.insertBefore(info, head.firstChild);
      head.insertBefore(doc.createTextNode('Component Diagram '), info);
    }
  }
  if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', function () { setTimeout(run, 0); });
  else setTimeout(run, 0);
})();

/* Round 9 (owner's note, 2026-10-18): Full and Summary are the Features tab's own tabs, so they are tabs: a tablist,
   each a tab marked selected (c-layout marks them pressed; the view lives in the bar's features-table-mode class),
   arrow keys, Home and End moving between them, and the tab drawn one step below the report's (c-round9.css). */
(function () {
  var doc = document;
  function $(s, r) { return (r || doc).querySelector(s); }
  function run() {
    var sw = $('.view-switch'), nav = $('.toolbar-row.report-nav');
    if (!sw || !nav) return;
    var tabs = Array.prototype.slice.call(sw.querySelectorAll('.view-btn'));
    if (tabs.length < 2) return;
    var cards = $('#report-content'), table = $('.features-table-view');
    if (table && !table.id) table.id = 'features-table-view';
    sw.setAttribute('role', 'tablist');
    tabs.forEach(function (t, i) {
      t.setAttribute('role', 'tab');
      var panel = i === 0 ? cards : table;
      if (panel && panel.id) t.setAttribute('aria-controls', panel.id);
    });
    function sync() {
      var summary = nav.classList.contains('features-table-mode');
      tabs.forEach(function (t, i) {
        var on = (i === 1) === summary;
        t.setAttribute('aria-selected', String(on));
        t.removeAttribute('aria-pressed');
        t.tabIndex = on ? 0 : -1;
      });
    }
    tabs.forEach(function (t) { t.addEventListener('click', function () { setTimeout(sync, 0); }); });
    sw.addEventListener('keydown', function (e) {
      var shown = tabs.filter(function (t) { return !t.hidden; });
      var i = shown.indexOf(doc.activeElement);
      if (i < 0) return;
      var j = e.key === 'ArrowRight' ? (i + 1) % shown.length : e.key === 'ArrowLeft' ? (i - 1 + shown.length) % shown.length : e.key === 'Home' ? 0 : e.key === 'End' ? shown.length - 1 : -1;
      if (j < 0) return;
      e.preventDefault();
      shown[j].click();
      shown[j].focus();
    });
    sync();
  }
  if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', function () { setTimeout(run, 0); });
  else setTimeout(run, 0);
})();
