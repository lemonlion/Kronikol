/* Option C preview, round 2: the report's page structure.
   - one compact run band: the ring, the test execution summary and CI on a single row
   - the filtering box on its own row
   - report tabs where the old action row was: Features, Timeline, Components, History, Diagnostics
   - the Features tab's own tools: Full / Summary, Sort, Expand all, and the report-level note controls
   Runs after c-dom.js, at the end of <body>, before DOMContentLoaded. Moves existing nodes; the old
   header containers stay in the document, hidden, so no script of the report loses an element. */
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
  function button(cls, text) { var b = el('button', cls, text); b.type = 'button'; return b; }

  /* ---------- 1. the run band ---------- */
  var oldRow = $('.header-row');
  var summaryTable = $('.test-execution-summary table');
  var ciTable = $('.ci-metadata table');
  var chart = $('.summary-chart svg');
  var band = null;

  function stat(label, value, cls) {
    var d = el('div', 'run-stat' + (cls ? ' ' + cls : ''));
    d.appendChild(el('dt', null, label));
    var dd = el('dd');
    if (typeof value === 'string') dd.textContent = value; else dd.appendChild(value);
    d.appendChild(dd);
    return d;
  }
  var PROVIDERS = { githubactions: 'GitHub Actions', azuredevops: 'Azure DevOps', gitlab: 'GitLab CI', gitlabci: 'GitLab CI', teamcity: 'TeamCity', jenkins: 'Jenkins', circleci: 'CircleCI', bitbucket: 'Bitbucket Pipelines' };

  if (oldRow && summaryTable) {
    var pairs = [];
    $$('tr', summaryTable).forEach(function (tr) {
      if (tr.style.display === 'none') return;
      for (var i = 0; i + 1 < tr.cells.length; i += 2) {
        if (tr.cells[i].classList.contains('column-header')) continue;
        var label = tr.cells[i].textContent.replace(/:\s*$/, '').trim();
        if (label) pairs.push({ label: label, cell: tr.cells[i + 1], used: false });
      }
    });
    var take = function (label) {
      for (var i = 0; i < pairs.length; i++) if (pairs[i].label.toLowerCase() === label.toLowerCase()) { pairs[i].used = true; return pairs[i].cell.textContent.trim(); }
      return null;
    };
    var utc = function (v) { return v ? v.replace(/\s*\(UTC\)\s*$/, '') : v; };

    band = el('section', 'run-band');
    band.setAttribute('aria-label', 'Test execution summary');
    if (chart) {
      var ring = el('div', 'run-ring');
      ring.appendChild(chart.cloneNode(true));
      band.appendChild(ring);
    }

    var zone = el('div', 'run-zone run-summary');
    var head = el('div', 'run-head');
    head.appendChild(el('h2', 'run-title', 'Test Execution Summary'));
    var status = take('Overall status');
    if (status) head.appendChild(el('span', 'run-status run-status-' + status.toLowerCase().replace(/[^a-z]+/g, '-'), status));
    var date = utc(take('Start Date')), start = utc(take('Start Time')), end = utc(take('End Time'));
    var when = [date, start && end ? start + '–' + end + ' UTC' : start ? start + ' UTC' : null].filter(Boolean).join(' · ');
    if (when) head.appendChild(el('span', 'run-when', when));
    zone.appendChild(head);

    var stats = el('dl', 'run-stats');
    [['Scenarios', 'Scenarios'], ['Passed Scenarios', 'Passed', 'run-stat-pass'], ['Failed Scenarios', 'Failed', 'run-stat-fail'],
     ['Skipped Scenarios', 'Skipped'], ['Bypassed Scenarios', 'Bypassed'], ['Features', 'Features'], ['Duration', 'Duration']]
      .forEach(function (s) {
        var v = take(s[0]);
        if (v == null) return;
        var cls = s[2] === 'run-stat-fail' ? (parseFloat(v) > 0 ? s[2] : '') : (s[2] || '');
        stats.appendChild(stat(s[1], v, cls));
      });
    pairs.forEach(function (p) { if (!p.used) stats.appendChild(stat(p.label.replace(/\s+Scenarios$/, ''), p.cell.textContent.trim())); });
    zone.appendChild(stats);
    band.appendChild(zone);

    if (ciTable) {
      var ci = el('div', 'run-zone run-ci');
      var ciHead = el('div', 'run-head');
      ciHead.appendChild(el('h2', 'run-title', 'CI'));
      var hdr = $('.column-header', ciTable);
      var provider = hdr && (hdr.textContent.match(/\(([^)]+)\)/) || [])[1];
      if (provider) ciHead.appendChild(el('span', 'run-when', PROVIDERS[provider.toLowerCase()] || provider));
      ci.appendChild(ciHead);
      var ciStats = el('dl', 'run-stats');
      var runLink = null;
      $$('tr', ciTable).forEach(function (tr) {
        if (tr.cells.length < 2) return;
        var label = tr.cells[0].textContent.replace(/:\s*$/, '').trim();
        var cell = tr.cells[1];
        var a = cell.querySelector('a');
        if (a) { runLink = a.cloneNode(true); runLink.textContent = 'View run'; runLink.className = 'run-link'; return; }
        var value = cell.firstElementChild ? cell.firstElementChild.cloneNode(true) : cell.textContent.trim();
        if (/^build\s*#$/i.test(label)) { label = 'Build'; if (typeof value === 'string') value = '#' + value; }
        ciStats.appendChild(stat(label, value));
      });
      if (runLink) ciStats.appendChild(stat('Run', runLink));
      ci.appendChild(ciStats);
      band.appendChild(ci);
    }

    oldRow.parentNode.insertBefore(band, oldRow);
    // the filtering box takes its own full-width row under the band
    var box = $('.filtering-box', oldRow);
    if (box) band.parentNode.insertBefore(box, oldRow);
    oldRow.hidden = true;

    // Ring, summary and CI share one row while all three fit whole; otherwise CI takes a second row under
    // the summary. Decided on measured widths, never by watching the wrap, so the switch cannot oscillate.
    var bandNeeds = null;
    var naturalZone = function (node) {
      var c = node.cloneNode(true);
      c.style.cssText += ';position:absolute;visibility:hidden;left:-99999px;top:0;width:max-content;padding-left:0;border-left:none;';
      band.appendChild(c);
      var w = c.getBoundingClientRect().width;
      c.remove();
      return w;
    };
    var bandLayout = function () {
      var sum = $('.run-summary', band), ciZone = $('.run-ci', band);
      if (!ciZone) return;
      if (bandNeeds == null) bandNeeds = ($('.run-ring', band) ? 64 + 24 : 0) + naturalZone(sum) + 24 + 25 + naturalZone(ciZone);
      var cs = getComputedStyle(band);
      var avail = band.clientWidth - parseFloat(cs.paddingLeft) - parseFloat(cs.paddingRight);
      band.classList.toggle('ci-below', bandNeeds > avail);
    };
    window.addEventListener('resize', bandLayout);
    doc.addEventListener('DOMContentLoaded', function () { bandNeeds = null; bandLayout(); });
    if (doc.fonts && doc.fonts.ready) doc.fonts.ready.then(function () { bandNeeds = null; bandLayout(); });
    bandLayout();
  }

  /* ---------- 2. report tabs and panels ---------- */
  var nav = $('.toolbar-row');
  var content = $('#report-content');
  if (!nav || !content) return;
  var left = $('.toolbar-left', nav), right = $('.toolbar-right', nav);
  nav.classList.add('report-nav');

  var tabs = el('div', 'report-tabs');
  tabs.setAttribute('role', 'tablist');
  tabs.setAttribute('aria-label', 'Report sections');
  var panels = {}, tabButtons = {};
  var panelHost = el('div', 'report-panels');
  nav.parentNode.insertBefore(panelHost, nav.nextSibling);

  function addTab(key, label, node, count) {
    if (!node) return;
    var b = button('report-tab', label);
    b.id = 'tab-' + key;
    b.setAttribute('role', 'tab');
    b.setAttribute('aria-controls', 'panel-' + key);
    b.setAttribute('data-tab', key);
    if (count != null) { var n = el('span', 'tab-n', String(count)); b.appendChild(n); }
    tabs.appendChild(b);
    var p = el('div', 'report-panel');
    p.id = 'panel-' + key;
    p.setAttribute('role', 'tabpanel');
    p.setAttribute('aria-labelledby', b.id);
    p.setAttribute('data-panel', key);
    p.appendChild(node);
    panelHost.appendChild(p);
    panels[key] = p;
    tabButtons[key] = b;
  }

  // Features: the feature cards and the summary table, one view at a time
  var featuresPanel = el('div', 'features-panel');
  var tableView = el('div', 'features-table-view');
  tableView.hidden = true;
  var oldSummary = $('.features-summary-details');
  var tableWrap = oldSummary && $('.features-summary-table-wrapper', oldSummary);
  if (tableWrap) { tableView.appendChild(tableWrap); oldSummary.hidden = true; }
  featuresPanel.appendChild(tableView);
  featuresPanel.appendChild(content);
  var features = $$(':scope > details.feature', content);
  addTab('features', 'Features', featuresPanel, features.length);
  addTab('timeline', 'Timeline', $('#scenario-timeline'));
  addTab('components', 'Components', $('#component-diagram'));
  var history = $('#history-section');
  addTab('history', 'History', history);
  var diagnostics = $('details.report-diagnostics');
  addTab('diagnostics', 'Diagnostics', diagnostics, diagnostics ? $$('.report-diagnostics-list > li', diagnostics).length : null);

  // History and Diagnostics are always open inside their tab: the tab is the disclosure now
  [history, diagnostics].forEach(function (d) {
    if (!d) return;
    d.open = true;
    var s = $(':scope > summary', d);
    if (s) {
      s.addEventListener('click', function (e) { e.preventDefault(); });
      s.setAttribute('tabindex', '-1');
    }
  });

  /* ---------- 3. the Features tab's tools ---------- */
  var tools = el('div', 'features-tools');
  var line = el('div', 'toolbar-left features-tools-line');
  var viewSwitch = el('div', 'view-switch');
  viewSwitch.setAttribute('role', 'group');
  viewSwitch.setAttribute('aria-label', 'Features view');
  var bFull = button('view-btn', 'Full'), bTable = button('view-btn', 'Summary');
  viewSwitch.appendChild(bFull);
  viewSwitch.appendChild(bTable);
  line.appendChild(viewSwitch);

  // Sort: the summary table's columns, applied to the feature cards and to the table's rows alike
  var sortSel = el('select', 'sort-select');
  sortSel.id = 'features-sort';
  sortSel.setAttribute('aria-label', 'Sort features');
  var table = tableWrap && $('table', tableWrap);
  // The table repeats Passed / Failed / Skipped for scenarios and for steps: name the three groups in a
  // row above, and call each group's first column Total. Clicking a column still sorts by it.
  if (table && table.tHead && table.tHead.rows.length === 1) {
    var hr = table.tHead.rows[0];
    var names = Array.prototype.map.call(hr.cells, function (c) { return c.textContent.trim(); });
    var sCol = names.indexOf('Steps'), dCol = names.indexOf('Duration');
    if (sCol > 1 && dCol > sCol) {
      var groups = doc.createElement('tr');
      groups.className = 'table-groups';
      [['', 1], ['Scenarios', sCol - 1], ['Steps', dCol - sCol], ['Duration', names.length - dCol]].forEach(function (g, i) {
        var th = doc.createElement('th');
        th.colSpan = g[1];
        th.textContent = g[0];
        if (i > 1) th.className = 'grp-start';
        groups.appendChild(th);
      });
      table.tHead.insertBefore(groups, hr);
      [1, sCol, dCol].forEach(function (c) { hr.cells[c].textContent = 'Total'; });
      $$('tr', table).forEach(function (tr) {
        if (tr === groups) return;
        [sCol, dCol].forEach(function (c) { if (tr.cells[c]) tr.cells[c].classList.add('grp-start'); });
      });
    }
  }
  var rowByName = {};
  if (table) $$('tbody tr', table).forEach(function (tr) { rowByName[tr.cells[0].textContent.trim()] = tr; });
  function featureName(f) {
    var s = $(':scope > summary', f);
    if (!s) return '';
    var t = s.firstChild && s.firstChild.nodeType === 3 ? s.firstChild.textContent : s.textContent;
    return t.trim();
  }
  features.forEach(function (f, i) { f._kOrder = i; f._kName = featureName(f); });
  function cellNumber(tr, col) {
    var c = tr && tr.cells[col];
    if (!c) return 0;
    var v = parseFloat(c.getAttribute('data-sort') || c.textContent);
    return isNaN(v) ? 0 : v;
  }
  var SORTS = [
    ['order', 'Sort: Feature A–Z', null],
    ['failed', 'Sort: Failures first', 3],
    ['scenarios', 'Sort: Most scenarios', 1],
    ['duration', 'Sort: Slowest total', 9],
    ['average', 'Sort: Slowest average', 10],
    ['longest', 'Sort: Longest scenario', 11]
  ];
  SORTS.forEach(function (s) { if (s[2] == null || table) { var o = el('option', null, s[1]); o.value = s[0]; sortSel.appendChild(o); } });
  function applySort(key) {
    var spec = SORTS.filter(function (s) { return s[0] === key; })[0];
    var col = spec ? spec[2] : null;
    var score = function (name) { return col == null ? 0 : -cellNumber(rowByName[name], col); };
    var sorted = features.slice().sort(function (a, b) { return (score(a._kName) - score(b._kName)) || (a._kOrder - b._kOrder); });
    sorted.forEach(function (f) { content.appendChild(f); });
    if (table) {
      var tbody = table.tBodies[0];
      var rows = Array.prototype.slice.call(tbody.rows);
      var order = {};
      sorted.forEach(function (f, i) { order[f._kName] = i; });
      rows.sort(function (a, b) {
        var ia = order[a.cells[0].textContent.trim()], ib = order[b.cells[0].textContent.trim()];
        return (ia == null ? 1e9 : ia) - (ib == null ? 1e9 : ib);
      }).forEach(function (r) { tbody.appendChild(r); });
      table.removeAttribute('data-sort-col');
      table.removeAttribute('data-sort-dir');
    }
  }
  sortSel.addEventListener('change', function () { applySort(sortSel.value); });
  line.appendChild(sortSel);
  line.appendChild(el('span', 'tools-spacer'));

  // Expand all features / scenarios move here; the panel toggles they sat beside are tabs now
  if (left) $$('.collapse-expand-all', left).forEach(function (b) { line.appendChild(b); });
  var gear = button('tools-gear', '⚙');
  gear.setAttribute('aria-label', 'Display settings');
  gear.title = 'Display settings';
  gear.setAttribute('aria-expanded', 'false');
  gear.addEventListener('click', function () {
    var open = nav.classList.toggle('tools-open');
    gear.setAttribute('aria-expanded', String(open));
  });
  line.appendChild(gear);
  if (left) { left.hidden = true; left.classList.remove('toolbar-left'); left.classList.add('toolbar-left-retired'); }

  tools.appendChild(line);
  if (right) tools.appendChild(right);
  nav.insertBefore(tabs, nav.firstChild);
  nav.appendChild(tools);
  if (left) nav.appendChild(left);

  function setView(v) {
    var table = v === 'table';
    bFull.setAttribute('aria-pressed', String(!table));
    bTable.setAttribute('aria-pressed', String(table));
    tableView.hidden = !table;
    content.hidden = table;
    nav.classList.toggle('features-table-mode', table);
    if (window.dispatchEvent) window.dispatchEvent(new Event('resize'));
  }
  bFull.addEventListener('click', function () { setView('full'); });
  bTable.addEventListener('click', function () { setView('table'); });
  setView('full');
  if (!table) bTable.hidden = true;

  // a row of the table opens its feature in the full view
  if (table) {
    table.tBodies[0].addEventListener('click', function (e) {
      var tr = e.target.closest('tr');
      if (!tr) return;
      var name = tr.cells[0].textContent.trim();
      var f = features.filter(function (x) { return x._kName === name; })[0];
      if (!f) return;
      setView('full');
      f.open = true;
      f.scrollIntoView({ block: 'start' });
    });
  }

  /* ---------- 4. what the filters leave showing, for the Timeline and Components tabs ---------- */
  // Both tabs follow the filters by default (Show: Filtered), and say so in bold while they show less than the
  // whole run; Show: Full puts the whole run back. The scenarios are read from the page, since every filter
  // ends by setting display on scenarios and features, and c-dom.js announces a change ('kron-filter-change').
  var allScenarios = $$('details.scenario', content);
  var isShownNode = function (n) { return n.style.display !== 'none' && !n.hidden; };
  var scenarioVisible = function (s) { var f = s.closest('details.feature'); return isShownNode(s) && (!f || isShownNode(f)); };
  var visibleScenarios = function () { return allScenarios.filter(scenarioVisible); };
  // a parameterized scenario counts once per example row, as the run counts it
  var runsOf = function (s, onlyShown) {
    if (!s.classList.contains('scenario-parameterized')) return 1;
    var rows = $$('.param-test-table tbody tr[data-row-idx]', s);
    if (!rows.length) return 1;
    // a scenario can list its example rows in more than one table: each row index is one run
    return new Set((onlyShown ? rows.filter(isShownNode) : rows).map(function (r) { return r.getAttribute('data-row-idx'); })).size;
  };
  var runCount = function (list, onlyShown) { return list.reduce(function (a, s) { return a + runsOf(s, onlyShown); }, 0); };
  var scenarioTitle = function (s) {
    var sum = $(':scope > summary', s);
    var t = sum ? Array.prototype.filter.call(sum.childNodes, function (n) { return n.nodeType === 3 && n.textContent.trim(); })[0] : null;
    return t ? t.textContent.trim() : '';
  };
  var segmentGroup = function (state, label, name, options, onChange) {
    var g = el('span', 'tl-seg');
    g.setAttribute('role', 'group');
    g.setAttribute('aria-label', label);
    g.appendChild(el('span', 'tl-seg-label', label));
    options.forEach(function (o) {
      var b = button('tl-seg-btn', o[1]);
      b.setAttribute('data-' + name, o[0]);
      b.setAttribute('aria-pressed', String(state[name] === o[0]));
      b.addEventListener('click', function () {
        if (state[name] === o[0]) return;
        state[name] = o[0];
        $$('.tl-seg-btn', g).forEach(function (x) { x.setAttribute('aria-pressed', String(x === b)); });
        onChange();
      });
      g.appendChild(b);
    });
    return g;
  };
  var filteredNote = function (host, after) {
    var n = el('div', 'filtered-note');
    n.setAttribute('role', 'status');
    n.hidden = true;
    host.insertBefore(n, after ? after.nextSibling : host.firstChild);
    return n;
  };

  /* ---------- 5. the timeline: filtered or full; warm-up shown or left out; ordered with or without it ---------- */
  // Each row's seconds come from its bar's own title: "2.37 s, 2.26 s of it first-call warm-up: …", or "2.8s" for
  // a scenario that paid none. A row belongs to the scenario of the same name (a parameterized scenario has a row
  // per example). Bars are drawn on a scale set by the longest bar showing; with warm-up left out a bar is the
  // scenario's time after its warm-up, and the row reads that time.
  var timeline = $('#scenario-timeline');
  var tlRows = timeline ? $$(':scope > .timeline-row', timeline) : [];
  if (tlRows.length) {
    var byTitle = {};
    allScenarios.forEach(function (s) { var t = scenarioTitle(s); (byTitle[t] = byTitle[t] || []).push(s); });
    var seconds = function (v, unit) { return unit === 'ms' ? v / 1000 : v; };
    tlRows.forEach(function (r, i) {
      var bar = $('.timeline-bar', r), title = bar ? bar.getAttribute('title') || '' : '';
      var total = /^\s*([\d.]+)\s*(ms|s)\b/.exec(title), warm = /([\d.]+)\s*(ms|s) of it first-call warm-up/.exec(title);
      r._kTotal = total ? seconds(+total[1], total[2]) : null;
      r._kWarm = warm ? seconds(+warm[1], warm[2]) : 0;
      r._kOrder = i;
      var d = $('.timeline-duration', r);
      r._kDur = d ? d.textContent : '';
      var label = $('.timeline-label', r);
      r._kScenarios = byTitle[label ? (label.getAttribute('title') || label.textContent).trim() : ''] || null;
    });
    var hasWarm = tlRows.some(function (r) { return r._kWarm > 0; });
    var tlState = { view: 'filtered', warm: 'shown', order: 'with' };
    var net = function (r) { return Math.max(0, r._kTotal - r._kWarm); };
    var fmtSeconds = function (s) { return s < 1 ? Math.round(s * 1000) + 'ms' : s.toFixed(1) + 's'; };
    var tlNote = null;
    var applyTimeline = function () {
      var vis = new Set(visibleScenarios());
      var filtering = runCount(Array.from(vis), true) < runCount(allScenarios, false);
      var showing = function (r) { return tlState.view === 'full' || !r._kScenarios || r._kScenarios.some(function (s) { return vis.has(s); }); };
      var shownRows = tlRows.filter(showing);
      var withWarm = tlState.warm === 'shown';
      var metric = function (r) { return withWarm ? r._kTotal : net(r); };
      var max = Math.max.apply(null, shownRows.filter(function (r) { return r._kTotal != null; }).map(metric).concat([0])) || 1;
      var shownSet = new Set(shownRows);
      tlRows.forEach(function (r) {
        r.hidden = !shownSet.has(r);
        var bar = $('.timeline-bar', r), dur = $('.timeline-duration', r), wu = $('.timeline-warm-up', r);
        if (r._kTotal != null) {
          if (bar) bar.style.width = (metric(r) / max * 100).toFixed(1) + '%';
          if (dur) dur.textContent = withWarm ? r._kDur : fmtSeconds(net(r));
        }
        if (wu) wu.hidden = !withWarm;
      });
      var key = tlState.order === 'with' ? function (r) { return r._kTotal || 0; } : function (r) { return r._kTotal == null ? 0 : net(r); };
      tlRows.slice().sort(function (a, b) { return (key(b) - key(a)) || (a._kOrder - b._kOrder); })
        .forEach(function (r) { timeline.appendChild(r); });
      var partial = tlState.view === 'filtered' && filtering;
      tlNote.hidden = !partial;
      if (partial) tlNote.textContent = 'Filtered: this timeline shows the ' + shownRows.length + ' of ' + tlRows.length + ' scenarios that match the filters.';
    };
    var tlControls = el('div', 'timeline-controls');
    tlControls.appendChild(segmentGroup(tlState, 'Show', 'view', [['filtered', 'Filtered'], ['full', 'Full']], applyTimeline));
    if (hasWarm) {
      tlControls.appendChild(segmentGroup(tlState, 'Warm-up', 'warm', [['shown', 'Shown'], ['hidden', 'Hidden']], applyTimeline));
      tlControls.appendChild(segmentGroup(tlState, 'Order', 'order', [['with', 'With warm-up'], ['without', 'Without warm-up']], applyTimeline));
    }
    var tlHead = $(':scope > .timeline-header', timeline);
    if (tlHead) tlHead.appendChild(tlControls); else timeline.insertBefore(tlControls, timeline.firstChild);
    tlNote = filteredNote(timeline, tlHead || tlControls);
    applyTimeline();
    doc.addEventListener('kron-filter-change', function () { if (tlState.view === 'filtered') applyTimeline(); });
  }

  /* ---------- 6. the component diagram: filtered or full, direction, and the call summaries on its edges ---------- */
  // All three rewrite the diagram's own source on its way to the engine, through the report's pre-processing
  // hook, so the first render already reads the chosen form; the shim caches by source, so switching back is
  // immediate. Left to right is the source as written. The diagram's own title ("Component Diagram") is left out:
  // the tab names it. Filtered keeps the components the shown scenarios used (each scenario lists them in
  // data-dependencies, by the names the diagram gives them) and the connections between those; the call counts
  // on a connection are the whole run's, and the note says so.
  var cdHost = $('#component-diagram');
  var cdEl = cdHost && $('.plantuml-browser', cdHost);
  var cdDirty = false, cdRedraw = function () {};
  if (cdEl) {
    var cdState = { view: 'filtered', dir: 'lr', calls: 'shown' };
    var cdOriginal = null;
    var LR = 'left to right direction';
    var BS = String.fromCharCode(92);
    var EDGE = /^(\s*\S+\s+[^\s:]*>\s+\S+)\s+:\s+.*$/;
    var BARE_EDGE = /^\s*\S+\s+[^\s:]*>\s+\S+\s*$/;
    var EDGE_ENDS = /^\s*(\S+)\s+[^\s:]*>\s+(\S+)/;
    var NODE = /^\s*(rectangle|queue|database|actor|component|node|cloud|collections|entity|boundary|control|participant|usecase|frame|folder|storage|artifact)\s+"([^"]*)"\s+as\s+(\S+)/;
    var depsOf = function (list) {
      var names = new Set();
      list.forEach(function (s) { (s.getAttribute('data-dependencies') || '').split(',').forEach(function (d) { d = d.trim(); if (d) names.add(d); }); });
      return names;
    };
    var knownDeps = depsOf(allScenarios);
    var cdFiltering = function () { return runCount(visibleScenarios(), true) < runCount(allScenarios, false); };
    var componentVariant = function (src) {
      // the report writes the source with CRLF line ends, and '.' does not match the CR before '$'
      var lines = src.split(/\r?\n/).filter(function (l) { return !/^\s*title\s/.test(l); });
      if (cdState.view === 'filtered' && cdFiltering()) {
        var used = depsOf(visibleScenarios()), drop = new Set();
        lines.forEach(function (l) {
          var m = NODE.exec(l);
          if (!m) return;
          var name = m[2].replace(/\*\*/g, '').split(BS + 'n')[0].trim();
          if (knownDeps.has(name) && !used.has(name)) drop.add(m[3]);
        });
        lines = lines.filter(function (l) {
          var m = NODE.exec(l);
          if (m) return !drop.has(m[3]);
          var e = EDGE_ENDS.exec(l);
          return !(e && (drop.has(e[1]) || drop.has(e[2])));
        });
      }
      var hasLR = lines.some(function (l) { return l.trim() === LR; });
      if (cdState.dir === 'tb') lines = lines.filter(function (l) { return l.trim() !== LR; });
      else if (!hasLR) lines.splice(1, 0, LR);
      if (cdState.calls === 'hidden') {
        // without their labels, two edges between the same pair in the same colour are one edge
        var seen = {};
        lines = lines.map(function (l) { var m = EDGE.exec(l); return m ? m[1] : l; })
          .filter(function (l) { if (!BARE_EDGE.test(l)) return true; if (seen[l]) return false; return (seen[l] = true); });
      }
      return lines.join('\n');
    };
    var cdNote = null;
    var updateCdNote = function () {
      var partial = cdState.view === 'filtered' && cdFiltering();
      cdNote.hidden = !partial;
      if (partial) cdNote.textContent = 'Filtered: only the components and connections used by the ' + runCount(visibleScenarios(), true) +
        ' scenarios that match the filters. The call counts on the connections are for the whole run.';
    };
    // The report renders the component diagram's source untouched; this hook hands it the chosen form, always
    // made from the source as written (enqueueElement writes the processed text back to the element).
    var reportPreProcess = window._preProcessSource;
    window._preProcessSource = function (target, source) {
      if (target === cdEl) {
        if (!cdOriginal) cdOriginal = source;
        return componentVariant(cdOriginal);
      }
      return typeof reportPreProcess === 'function' ? reportPreProcess.apply(this, arguments) : source;
    };
    // Before the tab has drawn it once there is nothing to redraw: the first render reads the state.
    cdRedraw = function () {
      updateCdNote();
      cdDirty = false;
      if (!cdOriginal) return;
      cdEl.innerHTML = '';
      delete cdEl.dataset.queued;
      cdEl.setAttribute('data-plantuml', cdOriginal);
      if (typeof window._renderDiagramsInContainer === 'function') window._renderDiagramsInContainer(cdHost);
    };
    var cdHead = el('div', 'timeline-header component-header');
    var cdControls = el('div', 'timeline-controls');
    cdControls.appendChild(segmentGroup(cdState, 'Show', 'view', [['filtered', 'Filtered'], ['full', 'Full']], function () { cdRedraw(); }));
    cdControls.appendChild(segmentGroup(cdState, 'Direction', 'dir', [['lr', 'Left to right'], ['tb', 'Top to bottom']], function () { cdRedraw(); }));
    cdControls.appendChild(segmentGroup(cdState, 'Call summaries', 'calls', [['shown', 'Shown'], ['hidden', 'Hidden']], function () { cdRedraw(); }));
    cdHead.appendChild(cdControls);
    cdHost.insertBefore(cdHead, cdHost.firstChild);
    cdNote = filteredNote(cdHost, cdHead);
    updateCdNote();
    // a filter change redraws the diagram now if its tab is showing, or when the tab is next opened
    var cdTimer = null;
    doc.addEventListener('kron-filter-change', function () {
      if (cdState.view !== 'filtered') return;
      if (panels.components && !panels.components.hidden) {
        clearTimeout(cdTimer);
        cdTimer = setTimeout(cdRedraw, 150);
      } else {
        cdDirty = true;
        updateCdNote();
      }
    });
  }

  /* ---------- tab behaviour ---------- */
  var renderedComponents = false;
  function activate(key) {
    if (!panels[key]) return;
    Object.keys(panels).forEach(function (k) {
      var on = k === key;
      panels[k].hidden = !on;
      tabButtons[k].setAttribute('aria-selected', String(on));
      tabButtons[k].tabIndex = on ? 0 : -1;
    });
    nav.classList.toggle('on-features', key === 'features');
    if (key === 'timeline') { var t = $('#scenario-timeline'); if (t) t.style.display = ''; }
    if (key === 'components') {
      var cd = $('#component-diagram');
      if (cd) {
        cd.style.display = '';
        if (!renderedComponents && typeof window._renderDiagramsInContainer === 'function') { window._renderDiagramsInContainer(cd); renderedComponents = true; }
        else if (renderedComponents && cdDirty) cdRedraw();
      }
    }
    if (window.dispatchEvent) window.dispatchEvent(new Event('resize'));
  }
  tabs.addEventListener('click', function (e) {
    var b = e.target.closest('.report-tab');
    if (b) activate(b.getAttribute('data-tab'));
  });
  tabs.addEventListener('keydown', function (e) {
    var keys = Object.keys(tabButtons);
    var i = keys.indexOf(doc.activeElement && doc.activeElement.getAttribute('data-tab'));
    if (i < 0) return;
    var j = e.key === 'ArrowRight' ? (i + 1) % keys.length : e.key === 'ArrowLeft' ? (i - 1 + keys.length) % keys.length : e.key === 'Home' ? 0 : e.key === 'End' ? keys.length - 1 : -1;
    if (j < 0) return;
    e.preventDefault();
    activate(keys[j]);
    tabButtons[keys[j]].focus();
  });
  activate('features');

  // Anything that goes to a scenario goes to the Features tab first: Next Failure, history links,
  // arrow-key scenario focus. The capture listener runs before the report's own handlers.
  doc.addEventListener('click', function (e) {
    if (!e.target.closest) return;
    if (e.target.closest('.jump-to-failure, .history-link, .report-diagnostics a[href^="#"]')) {
      activate('features');
      setView('full');
    }
  }, true);
  doc.addEventListener('keydown', function (e) {
    if ((e.key === 'ArrowDown' || e.key === 'ArrowUp') && !/input|select|textarea/i.test((doc.activeElement || {}).tagName || '') && panels.features && panels.features.hidden) {
      activate('features');
    }
  }, true);
})();
