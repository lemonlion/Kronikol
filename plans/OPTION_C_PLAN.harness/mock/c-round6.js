
/* Round 6 (owner's notes, 2026-10-17, third batch): the DURATION buttons say they leave warm-up out, Dependencies
   and Tags each get a Clear, and the Features tools line puts what you see on the left and the order on the right. */
(function () {
  var doc = document;
  function $(s, r) { return (r || doc).querySelector(s); }
  function $$(s, r) { return Array.prototype.slice.call((r || doc).querySelectorAll(s)); }
  function fmt(ms) { return ms < 1000 ? Math.round(ms) + ' ms' : (ms / 1000).toFixed(1) + ' s'; }
  function run() {
    // DURATION: each threshold is a scenario's own time, without the first-call warm-up it paid (the report
    // already computes and compares it that way; now every button says so, not just the label)
    var group = $('.filtering-box .duration-filters');
    var anyWarm = $$('[data-warmup-ms]').some(function (e) { return parseFloat(e.getAttribute('data-warmup-ms')) > 0; });
    if (group && anyWarm) {
      var label = $('.duration-filters-label', group);
      if (label) label.title = "Each scenario's own time, without the first-call warm-up it paid";
      var share = { p50: 'the slowest half', p90: 'the slowest 10%', p95: 'the slowest 5%', p99: 'the slowest 1%' };
      $$('.percentile-btn', group).forEach(function (b) {
        if (b.getAttribute('data-custom') === '1') { b.title = 'Scenarios taking at least the seconds you enter, not counting first-call warm-up'; return; }
        var ms = parseFloat(b.getAttribute('data-threshold-ms')), key = null;
        Object.keys(share).forEach(function (k) { if (group.getAttribute('data-' + k) === b.getAttribute('data-threshold-ms')) key = key || k; });
        b.title = 'Scenarios taking ' + fmt(ms) + ' or more, not counting first-call warm-up' + (key ? ' (' + share[key] + ')' : '');
      });
    }

    // Dependencies and Tags: a quiet Clear at the end of the chips, there only while the group has a chip in or out.
    // It clicks each set chip back to off, so the report's own handlers, the URL hash and Happy Paths follow.
    [['.dependency-filters', '.dependency-toggle', 'dependency'], ['.tag-filters', '.tag-toggle', 'tag']].forEach(function (g) {
      var box = $('.filtering-box ' + g[0]);
      if (!box) return;
      var host = $('.fltr-controls', box) || box;
      var clr = doc.createElement('button');
      clr.type = 'button';
      clr.className = 'group-clear';
      clr.textContent = 'Clear';
      clr.title = 'Clear the ' + g[2] + ' filters';
      host.appendChild(clr);
      var isSet = function (b) { return b.classList.contains('dependency-active') || b.classList.contains('tag-active') || b.classList.contains('tagf-out'); };
      var refresh = function () { clr.hidden = !$$(g[1], box).some(isSet); };
      clr.addEventListener('click', function () {
        var chips = $$(g[1], box);
        chips.forEach(function (b) { for (var i = 0; i < 3 && isSet(b); i++) b.click(); });
        refresh();
        if (chips[0]) chips[0].focus();
      });
      doc.addEventListener('click', function () { setTimeout(refresh, 0); });
      doc.addEventListener('contextmenu', function () { setTimeout(refresh, 0); });
      doc.addEventListener('kron-filter-change', refresh);
      window.addEventListener('hashchange', function () { setTimeout(refresh, 0); });
      refresh();
    });

    // Features tools: [Full | Summary] [Expand all ...] on the left (how much you see), SORT at the right end (the
    // order, which holds for both views). The label is a label now, not a prefix in every option.
    var line = $('.features-tools-line'), sel = $('#features-sort');
    if (line && sel) {
      var spacer = $('.tools-spacer', line);
      var wrap = doc.createElement('label');
      wrap.className = 'sort-group';
      var lab = doc.createElement('span');
      lab.className = 'sort-label';
      lab.textContent = 'Sort';
      sel.parentNode.insertBefore(wrap, sel);
      wrap.appendChild(lab);
      wrap.appendChild(sel);
      $$('option', sel).forEach(function (o) { o.textContent = o.textContent.replace('Sort: ', ''); });
      $$('.collapse-expand-all', line).forEach(function (b) { line.insertBefore(b, spacer); });
      if (spacer) line.insertBefore(wrap, spacer.nextSibling);
    }
  }
  if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', function () { setTimeout(run, 0); });
  else setTimeout(run, 0);
})();
