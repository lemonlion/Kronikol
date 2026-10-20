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
            var item = { el: s, deps: d, status: s.getAttribute('data-status') || '', verdicts: hv ? new Set(hv.split(',')) : null, isHappy: s.classList.contains('happy-path'), f: features[fi], searchText: s.getAttribute('data-search') || '', runs: n, tag: false, dep: false, st: false, sr: false, dur: false, q: false };
// kron:else
//|            var item = { el: s, deps: d, status: s.getAttribute('data-status') || '', isHappy: s.classList.contains('happy-path'), f: features[fi], searchText: s.getAttribute('data-search') || '', runs: n, tag: false, dep: false, st: false, sr: false, dur: false, q: false };
// kron:/verdicts
            items.push(item);
            fMap.set(s, features[fi]);
        }
    }
    _filterCache = { items: items, features: features, scenarios: scenarios, fMap: fMap, runs: runs, shown: null };
    return _filterCache;
}
// A scenario's tags as its data-tags attribute carries them (5.0.0, plans/TAGS_PLAN.md §4.9): its feature's and its
// own, each in its chip's spelling, percent-encoded one by one and joined with commas, so that a comma, a quote or a
// space inside a name survives. A name that does not decode is kept as written.
function tag_names(el) {
    var raw = el.getAttribute('data-tags');
    if (!raw) return [];
    return raw.split(',').filter(Boolean).map(function (t) { try { return decodeURIComponent(t); } catch (e) { return t; } });
}
// The names @tag compares a scenario's against: the same tags, in lower case.
function search_tags(el) {
    var tags = new Set();
    tag_names(el).forEach(function (t) { tags.add(t.trim().toLowerCase()); });
    return tags;
}
// Every filter has a flag of its own on each scenario: the tags (with Happy paths), dependencies, status, the search,
// duration and the search's qualifiers. A scenario shows while no flag hides it, a rule or a feature while one of its
// scenarios shows. While an address's filters are put on together (apply_url_hash), the scenarios are shown or hidden
// once, at the end: each filter's own pass would tell the timeline and the components of every step on the way.
var _visibilityHold = 0, _visibilityHeld = null;
function hold_visibility() { _visibilityHold++; }
function release_visibility() {
    if (_visibilityHold > 0) _visibilityHold--;
    if (_visibilityHold > 0 || !_visibilityHeld) return;
    var c = _visibilityHeld;
    _visibilityHeld = null;
    applyVisibility(c);
}
function applyVisibility(c) {
    if (_visibilityHold > 0) { _visibilityHeld = c; return; }
    for (var i = 0; i < c.items.length; i++) {
        var d = c.items[i];
        var hidden = d.tag || d.dep || d.st || d.sr || d.dur || d.q;
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
