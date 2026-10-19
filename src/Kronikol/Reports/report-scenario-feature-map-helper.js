var _filterCache;
function fc() {
    if (_filterCache) return _filterCache;
    var scenarios = document.getElementsByClassName('scenario');
    var features = document.getElementsByClassName('feature');
    var items = [];
    var fMap = new Map();
    var runs = 0;
    for (var fi = 0; fi < features.length; fi++) {
        var sc = features[fi].getElementsByClassName('scenario');
        for (var si = 0; si < sc.length; si++) {
            var s = sc[si];
            var raw = s.getAttribute('data-dependencies') || '';
            var d = raw ? new Set(raw.split(',')) : new Set();
            // A parameterized scenario holds one run per example (5.0.0): what the filters leave showing is counted in
            // runs, as the run band counts scenarios.
            var n = parseInt(s.getAttribute('data-runs') || '1', 10) || 1;
            runs += n;
// kron:verdicts
            // The cross-run history verdicts, when the run had a ledger: what `$flaky` and `$broke` read.
            var hv = s.getAttribute('data-history-verdicts') || '';
            var item = { el: s, deps: d, status: s.getAttribute('data-status') || '', verdicts: hv ? new Set(hv.split(',')) : null, isHappy: s.classList.contains('happy-path'), f: features[fi], searchText: s.getAttribute('data-search') || '', runs: n, tag: false, dep: false, st: false, sr: false, dur: false, cat: false, q: false };
// kron:else
//|            var item = { el: s, deps: d, status: s.getAttribute('data-status') || '', isHappy: s.classList.contains('happy-path'), f: features[fi], searchText: s.getAttribute('data-search') || '', runs: n, tag: false, dep: false, st: false, sr: false, dur: false, cat: false, q: false };
// kron:/verdicts
            items.push(item);
            fMap.set(s, features[fi]);
        }
    }
    _filterCache = { items: items, features: features, scenarios: scenarios, fMap: fMap, runs: runs, shown: null };
    return _filterCache;
}
// Every filter has a flag of its own on each scenario: the tags (with Happy paths), dependencies, status, the search,
// duration, categories and the search's qualifiers. A scenario shows while no flag hides it, a rule or a feature while
// one of its scenarios shows.
function applyVisibility(c) {
    for (var i = 0; i < c.items.length; i++) {
        var d = c.items[i];
        var hidden = d.tag || d.dep || d.st || d.sr || d.dur || d.cat || d.q;
        d.el.style.display = hidden ? 'none' : '';
    }
    var rules = document.getElementsByClassName('rule');
    for (var i = 0; i < rules.length; i++) {
        var rs = rules[i].getElementsByClassName('scenario');
        var hasVisibleInRule = false;
        for (var j = 0; j < rs.length; j++) {
            if (rs[j].style.display !== 'none') { hasVisibleInRule = true; break; }
        }
        rules[i].style.display = hasVisibleInRule ? '' : 'none';
    }
    for (var i = 0; i < c.features.length; i++) {
        var f = c.features[i];
        var sc = f.getElementsByClassName('scenario');
        var hasVisible = false;
        for (var j = 0; j < sc.length; j++) {
            if (sc[j].style.display !== 'none') { hasVisible = true; break; }
        }
        f.style.display = hasVisible ? '' : 'none';
    }
    filter_readout(c);
}
// What the filters leave showing (5.0.0, plans/OPTION_C_PLAN.md S5, D14): the readout beside Clear All, and a
// `kron-filter-change` event on the document, { shown, total } in runs, whenever the scenarios shown change.
function filter_readout(c) {
    var shown = 0, signature = '', features = 0;
    for (var i = 0; i < c.items.length; i++) {
        var on = c.items[i].el.style.display !== 'none';
        if (on) shown += c.items[i].runs;
        signature += on ? '1' : '0';
    }
    for (var j = 0; j < c.features.length; j++) if (c.features[j].style.display !== 'none') features++;
    var readout = document.querySelector('.filter-readout');
    if (readout) {
        var filtered = shown < c.runs;
        var scenarios = c.runs === 1 ? ' scenario, ' : ' scenarios, ', ofFeatures = c.features.length === 1 ? ' feature' : ' features';
        readout.classList.toggle('is-filtered', filtered);
        readout.textContent = '';
        if (filtered) {
            var b = document.createElement('b');
            b.textContent = 'Filtered';
            readout.appendChild(b);
            readout.appendChild(document.createTextNode(' ' + shown + ' of ' + c.runs + scenarios + features + ' of ' + c.features.length + ofFeatures));
        } else {
            readout.appendChild(document.createTextNode('All ' + c.runs + scenarios + c.features.length + ofFeatures));
        }
    }
    if (c.shown === null) c.shown = new Array(c.items.length + 1).join('1');
    if (signature !== c.shown) {
        c.shown = signature;
        document.dispatchEvent(new CustomEvent('kron-filter-change', { detail: { shown: shown, total: c.runs } }));
    }
}
