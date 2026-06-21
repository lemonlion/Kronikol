var _filterCache;
function fc() {
    if (_filterCache) return _filterCache;
    var scenarios = document.getElementsByClassName('scenario');
    var features = document.getElementsByClassName('feature');
    var items = [];
    var fMap = new Map();
    for (var fi = 0; fi < features.length; fi++) {
        var sc = features[fi].getElementsByClassName('scenario');
        for (var si = 0; si < sc.length; si++) {
            var s = sc[si];
            var raw = s.getAttribute('data-dependencies') || '';
            var d = raw ? new Set(raw.split(',')) : new Set();
            var item = { el: s, deps: d, status: s.getAttribute('data-status') || '', isHappy: s.classList.contains('happy-path'), f: features[fi], searchText: s.getAttribute('data-search') || '', hp: false, dep: false, st: false, sr: false, dur: false, cat: false };
            items.push(item);
            fMap.set(s, features[fi]);
        }
    }
    _filterCache = { items: items, features: features, scenarios: scenarios, fMap: fMap };
    return _filterCache;
}
function applyVisibility(c) {
    for (var i = 0; i < c.items.length; i++) {
        var d = c.items[i];
        var hidden = d.hp || d.dep || d.st || d.sr || d.dur || d.cat;
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
}