function current_url_anchor() {
    // Two anchor forms name an element rather than filter state: `#scenario-<slug>`, which the
    // report's own link buttons write, and `#sid-<stableId>`, which `kronikol query` and Failures.md
    // print. The slug is the display name, so it moves on a rename and collides when two features
    // name a scenario the same thing; the stable id does neither. Filter state is `k=v&k=v`, so an
    // anchor rides in front of it as the first segment and survives every rewrite.
    var first = window.location.hash.substring(1).split('&')[0];
    return first.indexOf('scenario-') === 0 || first.indexOf('sid-') === 0 ? first : null;
}
function element_for_stable_id(id) {
    // An outline row is rendered TWICE — once in the flat parameter table and once in the grouped
    // one — and only one of the two tables is displayed. Both copies carry the stable id, so pick
    // the displayed one; a collapsed <details> is not "hidden" here, since opening it is the whole
    // point of following the link.
    var all = document.querySelectorAll('[data-stable-id="' + id + '"]');
    for (var i = 0; i < all.length; i++) {
        var hidden = false;
        for (var p = all[i]; p; p = p.parentElement) {
            if (p.style && p.style.display === 'none') { hidden = true; break; }
        }
        if (!hidden) return all[i];
    }
    return all.length > 0 ? all[0] : null;
}
function reveal_url_anchor(anchor) {
    var el = anchor.indexOf('sid-') === 0
        ? element_for_stable_id(anchor.substring(4))
        : (document.getElementById(anchor) || document.querySelector('tr[data-scenario-id="' + anchor + '"]'));
    if (!el) return;
    // Only the grouped copy of an outline row carries an `id`, so a slug anchor always lands there
    // even when the flat table is the one on display. Re-resolve through the stable id both rows
    // share so the row that gets selected is the row somebody can see.
    if (el.tagName === 'TR' && el.getAttribute('data-stable-id')) {
        el = element_for_stable_id(el.getAttribute('data-stable-id')) || el;
    }
    // Open EVERY enclosing <details>, not only the feature: an example row lives inside a
    // parameterized group that starts collapsed, so opening the feature alone scrolls to nothing.
    var p = el;
    while (p) { if (p.tagName === 'DETAILS') p.setAttribute('open', ''); p = p.parentElement; }
    var target = el;
    if (el.tagName === 'TR') {
        el.click();
        target = el.closest('details.scenario-parameterized') || el;
    }
    target.scrollIntoView({ behavior: 'smooth', block: 'center' });
}
function update_url_hash() {
    var parts = [];
    var search = document.getElementById('searchbar');
    if (search && search.value) parts.push('q=' + encodeURIComponent(search.value));
    var statuses = [];
    document.querySelectorAll('.status-toggle.status-active').forEach(function(b) { statuses.push(b.getAttribute('data-status')); });
    if (statuses.length > 0) parts.push('status=' + statuses.join(','));
    var deps = [];
    document.querySelectorAll('.dependency-toggle.dependency-active').forEach(function(b) { deps.push(b.getAttribute('data-dependency')); });
    if (deps.length > 0) parts.push('deps=' + encodeURIComponent(deps.join(',')));
    if (_depMode !== _depModeDefault) parts.push('depmode=' + _depMode);
    if (typeof _catMode !== 'undefined' && _catMode !== _catModeDefault) parts.push('catmode=' + _catMode);
    if (document.querySelector('.happy-path-toggle.happy-path-active')) parts.push('hp=1');
    var cats = [];
    if (!document.querySelector('.category-toggle.category-active[data-category=""]')) {
        document.querySelectorAll('.category-toggle.category-active').forEach(function(b) { cats.push(b.getAttribute('data-category')); });
    }
    if (cats.length > 0) parts.push('cats=' + encodeURIComponent(cats.join(',')));
    var dur = document.getElementById('duration-threshold');
    if (dur && dur.value) parts.push('dur=' + dur.value);
    var activeP = document.querySelector('.percentile-btn.percentile-active');
    if (activeP) parts.push('pctl=' + encodeURIComponent(activeP.textContent));
    // Filter state is rewritten; the anchor is not. Somebody who followed a link to one scenario and
    // then narrowed the list still wants that link to be what they can copy back out.
    var anchor = current_url_anchor();
    if (anchor) parts.unshift(anchor);
    var hash = parts.length > 0 ? '#' + parts.join('&') : '';
    history.replaceState(null, '', window.location.pathname + window.location.search + hash);
}
function parse_url_hash() {
    var hash = window.location.hash.substring(1);
    if (!hash) return;
    var anchor = current_url_anchor();
    var params = {};
    hash.split('&').forEach(function(p) {
        var kv = p.split('=');
        if (kv.length === 2) params[kv[0]] = decodeURIComponent(kv[1]);
    });
    if (params.q) {
        var sb = document.getElementById('searchbar');
        if (sb) { sb.value = params.q; run_search_scenarios(); }
    }
    if (params.status) {
        params.status.split(',').forEach(function(s) {
            var btn = document.querySelector('.status-toggle[data-status="' + s + '"]');
            if (btn) btn.classList.add('status-active');
        });
        filter_statuses();
    }
    // Both values are accepted symmetrically: a deep link from an AND-default report must
    // force AND on an OR-default report and vice versa (configured defaults mean either
    // value can differ from this report's own start mode).
    if (params.depmode === 'OR' || params.depmode === 'AND') {
        _depMode = params.depmode;
        var modeBtn = document.querySelector('.dep-mode-toggle');
        if (modeBtn) modeBtn.textContent = _depMode;
    }
    if (params.catmode === 'AND' || params.catmode === 'OR') {
        _catMode = params.catmode;
        var catModeBtn = document.querySelector('.cat-mode-toggle');
        if (catModeBtn) catModeBtn.textContent = _catMode;
    }
    if (params.deps) {
        params.deps.split(',').forEach(function(d) {
            var btn = document.querySelector('.dependency-toggle[data-dependency="' + d + '"]');
            if (btn) btn.classList.add('dependency-active');
        });
        filter_dependencies();
    }
    if (params.hp === '1') {
        var hp = document.querySelector('.happy-path-toggle');
        if (hp) { hp.classList.add('happy-path-active'); filter_happy_paths(); }
    }
    if (params.pctl) {
        document.querySelectorAll('.percentile-btn').forEach(function(b) {
            if (b.textContent === params.pctl) {
                b.classList.add('percentile-active');
                if (b.getAttribute('data-custom') === '1') {
                    var cw = document.getElementById('custom-duration-wrap');
                    if (cw) cw.style.display = 'inline-flex';
                }
            }
        });
    }
    if (params.dur) {
        var dur = document.getElementById('duration-threshold');
        if (dur) { dur.value = params.dur; filter_duration(); }
    }
    if (params.cats) {
        var allBtn = document.querySelector('.category-toggle[data-category=""]');
        if (allBtn) allBtn.classList.remove('category-active');
        params.cats.split(',').forEach(function(c) {
            var btn = document.querySelector('.category-toggle[data-category="' + c + '"]');
            if (btn) btn.classList.add('category-active');
        });
        filter_categories();
    }
    // Last, because a filter pass can collapse what the anchor has to open, and the scroll has to
    // land on the layout the filters produced rather than the one they replaced.
    if (anchor) reveal_url_anchor(anchor);
}
