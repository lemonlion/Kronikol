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
    jump_into_view(target, 'center');
}
function jump_into_view(target, block) {
    // At once, not smoothly. A feature or scenario has content-visibility: auto, so one the browser has
    // never drawn counts as its placeholder height (500 px for a closed feature about 60 px tall). A
    // smooth scroll is aimed once, across those placeholders, and they shrink to their true size as it
    // passes them: it ended past the target, at the bottom of the page. A jump draws only what it lands
    // among, but those shrink in the frames after it, which moves the target wherever the browser does
    // not anchor the scroll (Safari has no scroll anchoring), so the jump is re-aimed while that settles.
    // A target that grows downward, a diagram drawn inside it, does not move its top and is left alone.
    var frames = 10, aimed;
    function aim() {
        target.scrollIntoView({ behavior: 'instant', block: block });
        aimed = target.getBoundingClientRect().top;
    }
    aim();
    requestAnimationFrame(function settle() {
        if (Math.abs(target.getBoundingClientRect().top - aimed) > 1) aim();
        if (--frames > 0) requestAnimationFrame(settle);
    });
}
// The page's link carries every filter (5.0.0, plans/OPTION_C_PLAN.md S5, Q5: hash v2). Each 4.x parameter keeps its
// meaning, so a link copied from a 4.x report opens the same view: deps= and cats= are the chosen chips, hp=1 is Happy
// paths Only, depmode= and catmode= the modes. What 4.x could not say rides parameters of its own: depx=, catx=, tags=,
// tagx= for the chips left out and the chosen tags, tagmode=, and hpx=1 for Happy paths Hide. A chip for the scenarios
// with none is written by its reserved value (__none__, __uncategorized__, __untagged__).
function update_url_hash() {
    var parts = [];
    var search = document.getElementById('searchbar');
    if (search && search.value) parts.push('q=' + encodeURIComponent(search.value));
    var statuses = [];
    document.querySelectorAll('.status-toggle.status-active').forEach(function(b) { statuses.push(b.getAttribute('data-status')); });
    if (statuses.length > 0) parts.push('status=' + statuses.join(','));
    function list(key, values) { if (values.length > 0) parts.push(key + '=' + encodeURIComponent(values.join(','))); }
    list('deps', chip_values('dependency', 'in'));
    list('depx', chip_values('dependency', 'out'));
    if (_depMode !== _depModeDefault) parts.push('depmode=' + _depMode);
    if (typeof _catMode !== 'undefined' && _catMode !== _catModeDefault) parts.push('catmode=' + _catMode);
    var happy = find_chip('tag', HAPPY_PATH_TAG), happyState = happy ? chip_state(happy) : 'off';
    if (happyState === 'in') parts.push('hp=1');
    if (happyState === 'out') parts.push('hpx=1');
    list('cats', chip_values('category', 'in'));
    list('catx', chip_values('category', 'out'));
    function notHappy(v) { return v !== HAPPY_PATH_TAG; }
    list('tags', chip_values('tag', 'in').filter(notHappy));
    list('tagx', chip_values('tag', 'out').filter(notHappy));
    if (_tagMode !== _tagModeDefault) parts.push('tagmode=' + _tagMode);
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
        set_mode_button(document.querySelector('.dep-mode-toggle'), _depMode);
    }
    if (params.catmode === 'AND' || params.catmode === 'OR') {
        _catMode = params.catmode;
        set_mode_button(document.querySelector('.cat-mode-toggle'), _catMode);
    }
    if (params.tagmode === 'AND' || params.tagmode === 'OR') {
        _tagMode = params.tagmode;
        set_mode_button(document.querySelector('.tag-mode-toggle'), _tagMode);
    }
    // a 4.x report drew the participant of a diagram with no call as a dependency: a link that chose it means none
    function chips(family, value, state) {
        if (value === undefined) return false;
        value.split(',').forEach(function(v) {
            var b = find_chip(family, v);
            if (!b && family === 'dependency' && v === '(no interactions)') b = find_chip(family, '__none__');
            if (b) set_chip_state(b, state);
        });
        return true;
    }
    var deps = chips('dependency', params.deps, 'in') | chips('dependency', params.depx, 'out');
    if (deps || params.depmode) filter_dependencies();
    var tags = chips('tag', params.tags, 'in') | chips('tag', params.tagx, 'out');
    var happy = find_chip('tag', HAPPY_PATH_TAG);
    if (happy && (params.hp === '1' || params.hpx === '1')) { set_chip_state(happy, params.hp === '1' ? 'in' : 'out'); tags = true; }
    if (tags || params.tagmode) filter_tags();
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
    var cats = chips('category', params.cats, 'in') | chips('category', params.catx, 'out');
    if (cats || params.catmode) filter_categories();
    // Last, because a filter pass can collapse what the anchor has to open, and the scroll has to
    // land on the layout the filters produced rather than the one they replaced.
    if (anchor) reveal_url_anchor(anchor);
}
