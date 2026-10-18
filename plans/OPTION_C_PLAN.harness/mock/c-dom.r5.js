/* Option C preview: the markup changes C needs, applied to a real generated report at load.
   Production would emit this markup from ReportGenerator.cs; the preview moves the existing
   elements (never recreates them) so every handler the report already bound keeps working.
   Runs synchronously at the end of <body>, before DOMContentLoaded, so the report's own
   layout and mobile scripts see the C markup when they start. */
(function () {
  'use strict';
  var doc = document;
  function $(sel, root) { return (root || doc).querySelector(sel); }
  function $$(sel, root) { return Array.prototype.slice.call((root || doc).querySelectorAll(sel)); }
  function el(tag, cls, text) {
    var e = doc.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    return e;
  }
  var NONE = '(no interactions)';

  /* ---------- toolbars: W3 split rows, Detail / Format / Toggle zones ---------- */
  var MODE_LABELS = { expanded: 'Expanded', collapsed: 'Collapsed', truncated: 'Truncated' };

  function syncChip(btn) {
    var base = btn.getAttribute('data-chip-label');
    if (btn.textContent !== base) btn.textContent = base;
    var on = String(btn.getAttribute('data-shown') === 'true');
    if (btn.getAttribute('aria-pressed') !== on) btn.setAttribute('aria-pressed', on);
  }
  // Word-free chips: the report's scripts rewrite "Headers Shown" / "Headers Hidden" on every
  // click (they only ever write it, never read it), so the preview puts the bare word back.
  function chipify(btn) {
    if (btn.hasAttribute('data-chip-label')) return;
    btn.setAttribute('data-chip-label', (btn.textContent || '').replace(/\s+(Shown|Hidden)\s*$/, '').trim());
    syncChip(btn);
    new MutationObserver(function () { syncChip(btn); })
      .observe(btn, { childList: true, characterData: true, subtree: true, attributes: true, attributeFilter: ['data-shown', 'class'] });
  }

  function relabelDetails(scope) {
    // round 10/11: the label names what the radio acts on in the reader's words (the boxes beside the arrows hold
    // each call's request, response or message), and shows on a desktop too. Round 16 (owner): "Payloads", not
    // "Call details": the boxes also hold headers, but a payload is what a reader looks for in them
    $$('.details-radio-label', scope).forEach(function (l) { l.textContent = 'Payloads'; });
    $$('.details-radio > .details-radio-btn[data-state]', scope).forEach(function (b) {
      var s = b.getAttribute('data-state');
      if (MODE_LABELS[s]) b.textContent = MODE_LABELS[s];
    });
    $$('.truncate-lines-select option', scope).forEach(function (o) { o.textContent = o.value + ' LINES'; });
    $$('.note-width-select option', scope).forEach(function (o) {
      o.textContent = o.textContent.trim().toUpperCase();
    });
  }

  // Detail, then Format (how a note renders), then Toggle (which elements appear), then the rest.
  function buildZones(bar) {
    var radio = $(':scope > .details-radio', bar);
    var fmt = el('span', 'diagram-format');
    $$(':scope > .note-format-select, :scope > .note-width-select, :scope > .note-font-select', bar)
      .forEach(function (s) { fmt.appendChild(s); });
    var flags = el('span', 'diagram-flags');
    $$(':scope > .toggle-btn', bar).forEach(function (b) { flags.appendChild(b); chipify(b); });
    var rest = Array.prototype.slice.call(bar.children).filter(function (c) { return c !== radio; });
    if (radio) bar.appendChild(radio);
    if (fmt.children.length) bar.appendChild(fmt);
    if (flags.children.length) bar.appendChild(flags);
    rest.forEach(function (c) { bar.appendChild(c); });
    relabelDetails(bar);
  }

  function splitToolbar(t) {
    if (t.closest('.toolbar-row') || t.classList.contains('diagram-toggle-tabs') || t.classList.contains('diagram-options-bar')) return;
    $$(':scope > .diagram-toggle-spacer', t).forEach(function (s) { s.remove(); });
    var tabs = $$(':scope > .diagram-toggle-btn', t);
    if (tabs.length) {
      // The view switcher gets its own bar. It stays the toolbar right after the summary, which
      // is the one the report's layout script floats onto the title line when it fits.
      var bar = el('div', 'diagram-toggle diagram-toggle-tabs');
      bar.setAttribute('role', 'tablist');
      tabs.forEach(function (b) { b.setAttribute('role', 'tab'); bar.appendChild(b); });
      if (t.hasAttribute('data-layout')) bar.setAttribute('data-layout', t.getAttribute('data-layout'));
      t.parentNode.insertBefore(bar, t);
      t.removeAttribute('data-layout');
      t.style.marginTop = '';
      t.classList.add('diagram-options-bar');
    }
    buildZones(t);
  }

  $$('.diagram-toggle').forEach(splitToolbar);
  var topBar = $('.toolbar-row .toolbar-right');
  if (topBar) buildZones(topBar);

  /* ---------- filtering box ---------- */
  var box = $('.filtering-box');
  var cache = typeof window.fc === 'function' ? window.fc() : null;

  function wrapControls(row, label) {
    if ($(':scope > .fltr-controls', row)) return;
    var ctl = el('span', 'fltr-controls');
    Array.prototype.slice.call(row.childNodes).forEach(function (n) {
      if (n !== label && !(n.nodeType === 3 && !n.textContent.trim())) ctl.appendChild(n);
    });
    row.appendChild(ctl);
    return ctl;
  }

  function itemTags(d) {
    if (!d._tags) {
      var raw = d.el.getAttribute('data-labels') || '';
      var t = new Set(raw ? raw.split(',').map(function (s) { return s.trim(); }).filter(Boolean) : []);
      if (d.isHappy) t.add('Happy Path');
      d._tags = t;
    }
    return d._tags;
  }

  // The chips carry no count; the number stays in the tooltip.
  function countChip(btn, n) {
    btn.title = n + (n === 1 ? ' scenario' : ' scenarios');
  }

  if (box) {
    $$('.export-btn', box).forEach(function (b) {
      if (/Filtered /.test(b.textContent) && !b.querySelector('.wide-only'))
        b.innerHTML = b.innerHTML.replace('Filtered ', '<span class="wide-only">Filtered </span>');
    });

    [['.status-filters', '.status-filters-label', 'Status'],
     ['.duration-filters', '.duration-filters-label', 'Duration ≥'],
     ['.category-filters', '.category-filters-label', 'Categories'],
     ['.dependency-filters', '.dependency-filters-label', 'Dependencies']].forEach(function (r) {
      $$(r[0], box).forEach(function (row) {
        var label = $(':scope > ' + r[1], row);
        if (label) label.textContent = r[2];
        wrapControls(row, label);
      });
    });

    // DURATION pairs with STATUS whenever both runs fit whole, and drops whole when they do not.
    var frow = $('.filter-row', box), dur = $('.duration-filters', box);
    if (frow && dur && dur.parentNode !== frow) frow.appendChild(dur);

    // HAPPY PATHS is a labelled three-way control on the Status/Duration line, like DURATION: All, Only,
    // Exclude. It is the Happy Path chip of the Tags row under another face (see syncHappyPaths), so there is
    // one filter underneath, not two. The old toggle stays in the page, hidden: the URL hash and Clear All
    // still drive it by its class.
    var hp = $('.happy-path-filters', box);
    var hpBtn = hp && $('.happy-path-toggle', hp);
    if (hpBtn && frow) {
      hp.hidden = true;
      var hpGroup = el('div', 'happy-filters');
      hpGroup.appendChild(el('span', 'happy-filters-label', 'Happy paths'));
      var hpCtl = el('span', 'fltr-controls');
      hpCtl.setAttribute('role', 'group');
      hpCtl.setAttribute('aria-label', 'Happy paths');
      [['off', 'Show'], ['in', 'Only'], ['out', 'Hide']].forEach(function (o) {
        var b = el('button', 'happy-seg', o[1]);
        b.type = 'button';
        b.setAttribute('data-hp', o[0]);
        b.setAttribute('aria-pressed', String(o[0] === 'off'));
        hpCtl.appendChild(b);
      });
      hpGroup.appendChild(hpCtl);
      frow.appendChild(hpGroup);
    }

    /* Tags: one tri-state chip per tag (off, has, hasn't), Happy Path pinned first, Untagged last */
    if (cache) {
      var tagCounts = new Map(), untagged = 0;
      cache.items.forEach(function (d) {
        var t = itemTags(d);
        if (!t.size) untagged++;
        t.forEach(function (name) { tagCounts.set(name, (tagCounts.get(name) || 0) + 1); });
      });
      if (tagCounts.size) {
        var names = Array.from(tagCounts.keys()).sort(function (a, b) {
          if (a === 'Happy Path') return -1;
          if (b === 'Happy Path') return 1;
          return a.localeCompare(b);
        });
        // Own classes, styled like the category row: the report's scripts look up the first
        // '.dep-mode-toggle' in the document, so this switch must not answer to that name.
        var row = el('div', 'tag-filters');
        row.appendChild(el('span', 'tag-filters-label', 'Tags'));
        var ctl = el('span', 'fltr-controls');
        var mode = el('button', 'tag-mode-toggle', 'OR');
        mode.type = 'button';
        mode.title = 'OR: a scenario with any of the chosen tags. AND: a scenario with every chosen tag.';
        ctl.appendChild(mode);
        names.forEach(function (name) {
          var b = el('button', 'tag-toggle', name);
          b.type = 'button';
          b.setAttribute('data-tag', name);
          b.setAttribute('data-tag-state', 'off');
          countChip(b, tagCounts.get(name));
          ctl.appendChild(b);
        });
        var none = el('button', 'tag-toggle tagf-none', 'Untagged');
        none.type = 'button';
        none.setAttribute('data-none', '1');
        none.setAttribute('data-tag-state', 'off');
        countChip(none, untagged);
        ctl.appendChild(none);
        row.appendChild(ctl);
        var depRow = $('.dependency-filters', box);
        (depRow ? depRow.parentNode : $('.filters', box) || box).insertBefore(row, depRow || null);
      }
    }

    /* Dependencies: counts, and the dependency-free scenarios as a pseudo-chip at the end */
    var depCtl = $('.dependency-filters > .fltr-controls', box);
    if (depCtl && cache) {
      var noDeps = cache.items.filter(function (d) { return d.deps.size === 0 || d.deps.has(NONE); }).length;
      var pseudo = $('.dependency-toggle[data-dependency="' + NONE + '"]', depCtl);
      if (!pseudo && noDeps) {
        pseudo = el('button', 'dependency-toggle');
        pseudo.type = 'button';
        pseudo.setAttribute('data-dependency', NONE);
      }
      $$('.dependency-toggle', depCtl).forEach(function (b) {
        var v = b.getAttribute('data-dependency');
        if (v === NONE) return;
        countChip(b, cache.items.filter(function (d) { return d.deps.has(v); }).length);
      });
      if (pseudo) {
        pseudo.textContent = 'No dependencies';
        pseudo.classList.add('tagf-none');
        countChip(pseudo, noDeps);
        depCtl.appendChild(pseudo);
      }
    }

    // A count needs three digits of room in every chip, or the widest number would set the slot.
    var maxDigits = 1;
    $$('.fcnt', box).forEach(function (c) { maxDigits = Math.max(maxDigits, c.textContent.length); });
    box.style.setProperty('--kron-count-slot', Math.max(3, maxDigits) + 'ch');
  }

  /* ---------- tri-state filtering ---------- */
  var tagMode = 'OR';

  function setTagState(b, s) {
    b.setAttribute('data-tag-state', s);
    b.classList.toggle('tag-active', s === 'in');
    b.classList.toggle('tagf-out', s === 'out');
    b.setAttribute('aria-pressed', s === 'in' ? 'true' : s === 'out' ? 'mixed' : 'false');
  }

  // HAPPY PATHS shows the Happy Path chip's state: Show = off, Only = required, Hide = left out. The old
  // toggle mirrors "Only", which is all the URL hash records.
  function happyChip() { return $('.tag-toggle[data-tag="Happy Path"]'); }
  function syncHappyPaths() {
    var chip = happyChip();
    var state = chip ? chip.getAttribute('data-tag-state') || 'off' : 'off';
    $$('.happy-seg').forEach(function (b) { b.setAttribute('aria-pressed', String(b.getAttribute('data-hp') === state)); });
    var btn = $('.happy-path-toggle');
    if (!btn) return;
    btn.classList.toggle('happy-path-active', state === 'in');
    btn.setAttribute('aria-pressed', String(state === 'in'));
  }

  // The tag filter rides the item flag the old "Happy Paths Only" filter used: the toggle now works
  // through the Happy Path tag, so the two can never disagree.
  function filterTags() {
    syncHappyPaths();
    if (!cache) return;
    var req = [], exc = [], reqNone = false, excNone = false, required = 0;
    $$('.tag-toggle').forEach(function (b) {
      var s = b.getAttribute('data-tag-state'), isNone = b.hasAttribute('data-none');
      if (s === 'in') { required++; if (isNone) reqNone = true; else req.push(b.getAttribute('data-tag')); }
      if (s === 'out') { if (isNone) excNone = true; else exc.push(b.getAttribute('data-tag')); }
    });
    cache.items.forEach(function (d) {
      var t = itemTags(d), isUntagged = t.size === 0, ok = true;
      if (req.length || reqNone) {
        var hits = req.map(function (n) { return t.has(n); });
        if (reqNone) hits.push(isUntagged);
        ok = tagMode === 'AND' ? hits.every(Boolean) : hits.some(Boolean);
      }
      if (ok && (exc.some(function (n) { return t.has(n); }) || (excNone && isUntagged))) ok = false;
      d.hp = !ok;
    });
    window.applyVisibility(cache);
  }

  window.filter_dependencies = function () {
    var c = window.fc();
    var req = [], exc = [];
    $$('.dependency-toggle').forEach(function (b) {
      var v = b.getAttribute('data-dependency');
      if (b.classList.contains('dependency-active')) req.push(v);
      else if (b.classList.contains('tagf-out')) exc.push(v);
    });
    var all = (window._depMode || 'AND') === 'AND';
    c.items.forEach(function (d) {
      function has(v) { return v === NONE ? (d.deps.size === 0 || d.deps.has(NONE)) : d.deps.has(v); }
      var ok = true;
      if (req.length) ok = all ? req.every(has) : req.some(has);
      if (ok && exc.some(has)) ok = false;
      d.dep = !ok;
    });
    window.applyVisibility(c);
    if (typeof window.update_url_hash === 'function') window.update_url_hash();
  };

  // off -> has -> hasn't -> off. Alt-click or right-click goes straight to hasn't.
  function next(state, toExclude) {
    if (toExclude) return state === 'out' ? 'off' : 'out';
    return state === 'off' ? 'in' : state === 'in' ? 'out' : 'off';
  }
  function cycle(chip, toExclude) {
    if (chip.classList.contains('tag-toggle')) {
      setTagState(chip, next(chip.getAttribute('data-tag-state') || 'off', toExclude));
      filterTags();
      return;
    }
    var cur = chip.classList.contains('dependency-active') ? 'in' : chip.classList.contains('tagf-out') ? 'out' : 'off';
    var s = next(cur, toExclude);
    chip.classList.toggle('dependency-active', s === 'in');
    chip.classList.toggle('tagf-out', s === 'out');
    chip.setAttribute('aria-pressed', s === 'in' ? 'true' : s === 'out' ? 'mixed' : 'false');
    window.filter_dependencies();
  }
  function chipFrom(e) { return e.target.closest ? e.target.closest('.tag-toggle, .filtering-box .dependency-toggle') : null; }
  // The URL hash and Clear All still drive the old toggle by its class; read it back through the tag.
  window.filter_happy_paths = function () {
    var btn = $('.happy-path-toggle'), chip = happyChip();
    if (btn && chip) {
      var want = btn.classList.contains('happy-path-active');
      var has = chip.getAttribute('data-tag-state') === 'in';
      if (want !== has) setTagState(chip, want ? 'in' : 'off');
    }
    filterTags();
    if (typeof window.update_url_hash === 'function') window.update_url_hash();
  };
  doc.addEventListener('click', function (e) {
    var seg = e.target.closest ? e.target.closest('.happy-seg') : null;
    if (seg) {
      e.stopPropagation();
      e.preventDefault();
      var hpChip = happyChip();
      if (hpChip) {
        setTagState(hpChip, seg.getAttribute('data-hp'));
        filterTags();
        if (typeof window.update_url_hash === 'function') window.update_url_hash();
      }
      return;
    }
    var chip = chipFrom(e);
    if (chip) {
      e.stopPropagation();
      e.preventDefault();
      cycle(chip, e.altKey);
      return;
    }
    var mode = e.target.closest ? e.target.closest('.tag-mode-toggle') : null;
    if (mode) {
      tagMode = tagMode === 'OR' ? 'AND' : 'OR';
      mode.textContent = tagMode;
      filterTags();
    }
  }, true);
  doc.addEventListener('contextmenu', function (e) {
    var chip = chipFrom(e);
    if (!chip) return;
    e.preventDefault();
    cycle(chip, true);
  }, true);

  if (typeof window.clear_all_filters === 'function') {
    var clearAll = window.clear_all_filters;
    window.clear_all_filters = function () {
      $$('.tag-toggle').forEach(function (b) { setTagState(b, 'off'); });
      $$('.dependency-toggle.tagf-out').forEach(function (b) { b.classList.remove('tagf-out'); b.setAttribute('aria-pressed', 'false'); });
      var r = clearAll.apply(this, arguments);
      filterTags();
      return r;
    };
  }

  /* ---------- what the filters leave showing, beside Clear All ---------- */
  // Read from the page, not from the filter state: every filter (search, status, duration, tags,
  // dependencies, categories) ends by setting display on scenarios and features, so watching those
  // styles catches them all, including the report's own scripts.
  var clearBtn = box ? $$('.filtering-box-export .export-btn', box).filter(function (b) { return /clear_all_filters/.test(b.getAttribute('onclick') || ''); })[0] : null;
  var reportContent = $('#report-content');
  if (clearBtn && reportContent) {
    var readout = el('span', 'filter-readout');
    readout.setAttribute('role', 'status');
    clearBtn.parentNode.insertBefore(readout, clearBtn);
    // On a phone the filter rows fold under the report's Filters toggle, whose line is otherwise empty: the
    // readout rides that line, so it costs no height there either.
    var filterToggle = $('.mobile-filter-toggle', box);
    if (filterToggle && window.matchMedia) {
      var toggleLine = el('div', 'mobile-filter-line');
      filterToggle.parentNode.insertBefore(toggleLine, filterToggle);
      toggleLine.appendChild(filterToggle);
      var phone = window.matchMedia('(max-width: 768px)');
      var placeReadout = function () {
        if (phone.matches && getComputedStyle(filterToggle).display !== 'none') toggleLine.appendChild(readout);
        else if (readout.nextSibling !== clearBtn) clearBtn.parentNode.insertBefore(readout, clearBtn);
      };
      placeReadout();
      if (phone.addEventListener) phone.addEventListener('change', placeReadout);
    }
    var shown = function (n) { return n.style.display !== 'none' && !n.hidden; };
    var lastSignature = null;
    var updateReadout = function () {
      var feats = $$(':scope > details.feature', reportContent);
      var scen = $$('details.scenario', reportContent);
      var visF = feats.filter(shown);
      var visS = scen.filter(function (s) { var f = s.closest('details.feature'); return shown(s) && (!f || shown(f)); });
      // Scenarios are counted as the run counts them: a parameterized scenario is one per example row.
      var runs = function (s, onlyShown) {
        if (!s.classList.contains('scenario-parameterized')) return 1;
        var rows = $$('.param-test-table tbody tr[data-row-idx]', s);
        if (!rows.length) return 1;
        // a scenario can list its example rows in more than one table: each row index is one run
        return new Set((onlyShown ? rows.filter(shown) : rows).map(function (r) { return r.getAttribute('data-row-idx'); })).size;
      };
      var sum = function (list, onlyShown) { return list.reduce(function (a, s) { return a + runs(s, onlyShown); }, 0); };
      var totalRuns = sum(scen, false), shownRuns = sum(visS, true);
      var filtered = shownRuns < totalRuns;
      // The Timeline and Components tabs follow the filters: tell them when the shown set changes.
      var visSet = new Set(visS);
      var signature = scen.map(function (s) { return visSet.has(s) ? runs(s, true) : 0; }).join(',');
      if (signature !== lastSignature) {
        lastSignature = signature;
        doc.dispatchEvent(new CustomEvent('kron-filter-change', { detail: { shown: shownRuns, total: totalRuns } }));
      }
      readout.classList.toggle('is-filtered', filtered);
      readout.textContent = '';
      if (filtered) {
        readout.appendChild(el('b', null, 'Filtered'));
        readout.appendChild(doc.createTextNode(' ' + shownRuns + ' of ' + totalRuns + ' scenarios, ' + visF.length + ' of ' + feats.length + ' features'));
      } else {
        readout.appendChild(doc.createTextNode('All ' + totalRuns + ' scenarios, ' + feats.length + ' features'));
      }
    };
    var readoutQueued = false;
    new MutationObserver(function () {
      if (readoutQueued) return;
      readoutQueued = true;
      requestAnimationFrame(function () { readoutQueued = false; updateReadout(); });
    }).observe(reportContent, { attributes: true, attributeFilter: ['style', 'hidden'], subtree: true });
    updateReadout();
  }

  /* ---------- a scenario's note options belong to its sequence diagrams ---------- */
  // Detail, Format and the Toggle chips act on sequence diagrams only, so the options bar shows while the
  // Sequence Diagrams tab is selected and steps aside for Activity Diagrams and Flame Chart.
  function syncOptionsBar(tabsBar) {
    // on a phone the report's settings button sits between the two bars
    var bar = tabsBar.nextElementSibling;
    while (bar && !bar.classList.contains('diagram-options-bar') && !bar.classList.contains('k-scn-settings') && !bar.classList.contains('diagram-view')) bar = bar.nextElementSibling;
    // round 17 item 26: the bar sits in its scenario's settings row, behind the gear
    if (bar && bar.classList.contains('k-scn-settings')) bar = bar.querySelector(':scope > .diagram-options-bar');
    if (!bar || !bar.classList.contains('diagram-options-bar')) return;
    var active = $('.diagram-toggle-btn.diagram-toggle-active', tabsBar);
    var seq = !active || active.getAttribute('data-dtype') === 'seq';
    bar.classList.toggle('not-seq', !seq);
    if (tabsBar.parentNode) tabsBar.parentNode.classList.toggle('diagrams-not-seq', !seq);
  }
  $$('.diagram-toggle-tabs').forEach(function (tabsBar) {
    syncOptionsBar(tabsBar);
    new MutationObserver(function () { syncOptionsBar(tabsBar); })
      .observe(tabsBar, { attributes: true, attributeFilter: ['class'], subtree: true });
  });

  /* ---------- labels: C's small-caps labels carry no colon ---------- */
  $$('.diff-label').forEach(function (l) { l.textContent = l.textContent.replace(/:\s*$/, ''); });

  /* ---------- measured layout: decided on the real labels, never on a guessed constant ---------- */
  // Width of an element laid out on one line at its natural size, measured on a hidden clone.
  function naturalWidth(node, prepare) {
    var clone = node.cloneNode(true);
    $$('[id]', clone).forEach(function (n) { n.removeAttribute('id'); });
    clone.style.cssText += ';position:absolute;visibility:hidden;left:-99999px;top:0;width:max-content;max-width:none;flex-wrap:nowrap;';
    if (prepare) prepare(clone);
    node.parentNode.appendChild(clone);
    var w = clone.getBoundingClientRect().width;
    clone.remove();
    return w;
  }
  function contentWidth(node) {
    var cs = getComputedStyle(node);
    return node.clientWidth - parseFloat(cs.paddingLeft) - parseFloat(cs.paddingRight);
  }
  var needs = null;
  function measureNeeds() {
    var status = $('.filter-row > .status-filters'), dur = $('.filter-row > .duration-filters');
    var happy = $('.filter-row > .happy-filters');
    // folded under the phone's Filters toggle every width reads 0 and everything would "fit": measure once it opens
    if (!status || !status.offsetParent) return;
    needs = {};
    var inlineLabel = function (sel) {
      return function (c) { var l = c.querySelector(sel); if (l) { l.style.flexBasis = 'auto'; l.style.marginRight = '8px'; } };
    };
    if (status && dur) {
      // STATUS and DURATION share the line when both runs fit whole; HAPPY PATHS joins them when all three do
      needs.pair = naturalWidth(status) + 28 + naturalWidth(dur, inlineLabel('.duration-filters-label'));
      if (happy) needs.trio = needs.pair + 28 + naturalWidth(happy, inlineLabel('.happy-filters-label'));
    }
  }
  function sameLine(a, b) {
    var ra = a.getBoundingClientRect(), rb = b.getBoundingClientRect();
    return rb.top < ra.bottom - 2 && rb.bottom > ra.top + 2;
  }
  function relayout() {
    if (!needs) measureNeeds();
    var frow = $('.filter-row'), fbox = $('.filtering-box');
    if (frow && fbox && needs && needs.pair) {
      var avail = contentWidth(fbox);
      frow.classList.toggle('pair-fits', needs.pair <= avail);
      frow.classList.toggle('trio-fits', !!needs.trio && needs.trio <= avail);
    }
    // anchored scrolls (Next Failure, history links, #sid- addresses) land below the pinned report bar
    var nav = $('.report-nav');
    if (nav) doc.documentElement.style.setProperty('--kron-nav-h', (getComputedStyle(nav).position === 'sticky' ? nav.offsetHeight : 0) + 'px');
    // a divider before the Toggle zone only where it sits right beside the zone before it
    $$('.diagram-flags').forEach(function (flags) {
      var prev = flags.previousElementSibling;
      if (!prev || !flags.offsetParent) return;
      var gap = flags.getBoundingClientRect().left - prev.getBoundingClientRect().right;
      flags.classList.toggle('zone-adjacent', sameLine(prev, flags) && gap < 40);
    });
  }
  var queued = false;
  function queue() {
    if (queued) return;
    queued = true;
    requestAnimationFrame(function () { queued = false; relayout(); });
  }
  window.addEventListener('resize', queue);
  doc.addEventListener('toggle', queue, true);
  doc.addEventListener('click', queue, true);
  doc.addEventListener('DOMContentLoaded', function () { needs = null; queue(); });
  window.addEventListener('load', function () { needs = null; queue(); });
  if (doc.fonts && doc.fonts.ready) doc.fonts.ready.then(function () { needs = null; queue(); });

  /* ---------- phones: the view tabs stay visible, the settings button moves onto the title line ---------- */
  function phoneFixups() {
    $$('.diagram-toggle-tabs').forEach(function (tabs) {
      var prev = tabs.previousElementSibling;
      if (prev && prev.classList.contains('scenario-diagram-controls-toggle')) prev.remove();
      tabs.classList.remove('scenario-diagram-controls');
      tabs.style.display = '';
    });
    $$('.diagram-options-bar').forEach(function (bar) {
      var gear = bar.previousElementSibling;
      while (gear && !gear.classList.contains('scenario-diagram-controls-toggle')) gear = gear.previousElementSibling;
      var summary = bar.parentNode && bar.parentNode.querySelector(':scope > summary');
      if (!gear || !summary || gear.classList.contains('gear-in-summary')) return;
      gear.classList.add('gear-in-summary');
      gear.setAttribute('aria-label', 'Diagram settings');
      gear.title = 'Diagram settings';
      summary.appendChild(gear);
      gear.addEventListener('click', function (e) { e.preventDefault(); });
      var keep = function () { if (gear.textContent !== '⚙') gear.textContent = '⚙'; gear.classList.toggle('gear-open', bar.style.display === 'flex'); };
      keep();
      new MutationObserver(keep).observe(gear, { childList: true, characterData: true, subtree: true });
      new MutationObserver(keep).observe(bar, { attributes: true, attributeFilter: ['style'] });
    });
  }
  doc.addEventListener('DOMContentLoaded', phoneFixups);
  window.addEventListener('load', phoneFixups);

  /* ---------- light or dark: the reader's choice, remembered; until they choose, the system's ---------- */
  // The boot script at the top of the page has already set data-theme before the first paint; this adds the
  // switch at the right of the title line, and follows the system while the reader has not chosen.
  var THEME_KEY = 'kronikol.theme';
  var storedTheme = function () { try { var t = localStorage.getItem(THEME_KEY); return t === 'dark' || t === 'light' ? t : null; } catch (e) { return null; } };
  var systemDark = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;
  var themeNow = function () { return doc.documentElement.getAttribute('data-theme') === 'dark' ? 'dark' : 'light'; };
  var themeBtn = null;
  var syncThemeButton = function () {
    if (!themeBtn) return;
    var dark = themeNow() === 'dark';
    themeBtn.setAttribute('aria-pressed', String(dark));
    themeBtn.title = dark ? 'Switch to the light theme' : 'Switch to the dark theme';
  };
  var setTheme = function (t, remember) {
    doc.documentElement.setAttribute('data-theme', t);
    if (remember) { try { localStorage.setItem(THEME_KEY, t); } catch (e) { /* private window: this page only */ } }
    syncThemeButton();
  };
  if (!doc.documentElement.hasAttribute('data-theme')) setTheme(storedTheme() || (systemDark && systemDark.matches ? 'dark' : 'light'), false);
  if (systemDark && systemDark.addEventListener) systemDark.addEventListener('change', function (e) { if (!storedTheme()) setTheme(e.matches ? 'dark' : 'light', false); });
  var SVGNS = 'http://www.w3.org/2000/svg';
  var icon = function (cls, paths) {
    var s = doc.createElementNS(SVGNS, 'svg');
    s.setAttribute('class', 'theme-icon ' + cls); s.setAttribute('viewBox', '0 0 24 24'); s.setAttribute('aria-hidden', 'true');
    paths.forEach(function (d) { var p = doc.createElementNS(SVGNS, 'path'); p.setAttribute('d', d); s.appendChild(p); });
    return s;
  };
  function addThemeSwitch() {
    if (themeBtn) return;
    var h1 = $('body > h1') || $('h1');
    if (!h1) return;
    var line = el('div', 'title-line');
    h1.parentNode.insertBefore(line, h1);
    line.appendChild(h1);
    themeBtn = el('button', 'theme-toggle');
    themeBtn.type = 'button';
    themeBtn.setAttribute('aria-label', 'Dark theme');
    // a moon while the page is light (the theme it switches to), a sun while it is dark
    themeBtn.appendChild(icon('theme-moon', ['M20.5 14.2A8.5 8.5 0 0 1 9.8 3.5a8.5 8.5 0 1 0 10.7 10.7z']));
    themeBtn.appendChild(icon('theme-sun', ['M12 7.5a4.5 4.5 0 1 0 0 9 4.5 4.5 0 0 0 0-9z', 'M12 1.8v2.4M12 19.8v2.4M4.8 4.8l1.7 1.7M17.5 17.5l1.7 1.7M1.8 12h2.4M19.8 12h2.4M4.8 19.2l1.7-1.7M17.5 6.5l1.7-1.7']));
    themeBtn.addEventListener('click', function () { setTheme(themeNow() === 'dark' ? 'light' : 'dark', true); });
    line.appendChild(themeBtn);
    syncThemeButton();
  }
  if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', addThemeSwitch); else addThemeSwitch();

  /* ---------- the report-wide note settings, remembered between visits ---------- */
  // Detail (Expanded, Collapsed, Truncated), the line count, the four Toggle chips, the payload format and the note
  // width. Saved after the report's own handler has run, and restored through the same handlers once the page has
  // loaded, so the report keeps its own state as it always has.
  var SETTINGS_KEY = 'kronikol.reportSettings';
  var reportBar = function () { return $('.toolbar-right'); };
  var readSettings = function () {
    var bar = reportBar();
    if (!bar) return null;
    var s = {};
    var active = $('.details-radio-btn[data-state].details-active', bar);
    if (active) s.details = active.getAttribute('data-state');
    var lines = $('.truncate-lines-select', bar); if (lines) s.lines = lines.value;
    $$('.toggle-btn[data-toggle]', bar).forEach(function (b) { s[b.getAttribute('data-toggle')] = b.getAttribute('data-shown') === 'true'; });
    var fmt = $('.note-format-select', bar); if (fmt) s.format = fmt.value;
    var width = $('.note-width-select', bar); if (width) s.width = width.value;
    return s;
  };
  var saveSettings = function () {
    var s = readSettings();
    if (s) { try { localStorage.setItem(SETTINGS_KEY, JSON.stringify(s)); } catch (e) { /* private window: nothing kept */ } }
  };
  var restoring = false;
  var restoreSettings = function () {
    var saved = null;
    try { saved = JSON.parse(localStorage.getItem(SETTINGS_KEY) || 'null'); } catch (e) { saved = null; }
    var bar = reportBar();
    if (!saved || !bar) return;
    restoring = true;
    try {
      var lines = $('.truncate-lines-select', bar);
      if (saved.lines && lines && lines.value !== saved.lines && typeof window._setTruncateLines === 'function'
          && Array.prototype.some.call(lines.options, function (o) { return o.value === saved.lines; })) {
        lines.value = saved.lines; window._setTruncateLines(lines);
      }
      var active = $('.details-radio-btn[data-state].details-active', bar);
      if (saved.details && typeof window._setReportDetails === 'function' && (!active || active.getAttribute('data-state') !== saved.details)
          && $('.details-radio-btn[data-state="' + saved.details + '"]', bar)) window._setReportDetails(saved.details);
      $$('.toggle-btn[data-toggle]', bar).forEach(function (b) {
        var want = saved[b.getAttribute('data-toggle')];
        if (typeof want === 'boolean' && (b.getAttribute('data-shown') === 'true') !== want) b.click();
      });
      [['.note-format-select', 'format', '_setNoteFormat'], ['.note-width-select', 'width', '_setNoteWidth']].forEach(function (k) {
        var sel = $(k[0], bar);
        if (saved[k[1]] && sel && sel.value !== saved[k[1]] && typeof window[k[2]] === 'function'
            && Array.prototype.some.call(sel.options, function (o) { return o.value === saved[k[1]]; })) {
          sel.value = saved[k[1]]; window[k[2]](sel);
        }
      });
    } finally { restoring = false; }
  };
  doc.addEventListener('click', function (e) {
    if (restoring || !e.target.closest) return;
    var b = e.target.closest('.toolbar-right .details-radio-btn');
    if (b) setTimeout(saveSettings, 0);
  });
  doc.addEventListener('change', function (e) {
    if (restoring || !e.target.closest) return;
    if (e.target.closest('.toolbar-right select')) setTimeout(saveSettings, 0);
  });
  window.addEventListener('load', function () { setTimeout(restoreSettings, 0); });

  doc.documentElement.classList.add('kron-c');
})();
