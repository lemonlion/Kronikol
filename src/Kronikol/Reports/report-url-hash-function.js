// Two anchor forms name an element rather than filter state: `#scenario-<slug>`, which the report's own link buttons
// write, and `#sid-<stableId>`, which `kronikol query` and Failures.md print. The slug is the display name, so it moves
// on a rename and collides when two features name a scenario the same thing; the stable id does neither. Filter state
// is `k=v&k=v`, so an anchor rides in front of it as the first segment and survives every rewrite. The one test of
// "the first segment names a scenario", for every script (5.2.0: the tabs and the views each had their own).
function url_anchor_of(hash) {
    var first = String(hash || '').replace(/^#/, '').split('&')[0];
    return first.indexOf('scenario-') === 0 || first.indexOf('sid-') === 0 ? first : null;
}
// The address the page shows: a write waiting its turn (report-navigation-script.js) is already the page's address.
function current_url_hash() {
    return window.kronikol_nav ? window.kronikol_nav.hash() : window.location.hash.replace(/^#/, '');
}
function current_url_anchor() {
    return url_anchor_of(current_url_hash());
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
// The scenario, rule or feature the filters hide around an element, the outermost one, or null when they hide none.
function filtered_out(el) {
    var hidden = null;
    for (var p = el; p && p.id !== 'report-content'; p = p.parentElement) {
        if (p.style && p.style.display === 'none' && p.classList
            && (p.classList.contains('scenario') || p.classList.contains('rule') || p.classList.contains('feature'))) hidden = p;
    }
    return hidden;
}
function drop_filtered_out_note() {
    var note = document.querySelector('.filtered-out-note');
    if (note) note.remove();
}
// A link to a scenario the filters hide (5.2.0, plans/BACK_BUTTON_NAVIGATION_PLAN.md S7, Q5): a line where it would be
// says so, with a button that clears the filters and shows it. Until 5.2.0 the link did nothing anyone could see. A
// Summary row for a feature they hide gives the feature's line, whose button shows it through the row (until 5.2.7 the
// row opened nothing anyone could see).
function filtered_out_note(hidden, anchor, noun, show) {
    noun = noun || 'scenario';
    drop_filtered_out_note();
    var note = document.createElement('div');
    note.className = 'filtered-out-note';
    var text = document.createElement('span');
    text.textContent = 'This ' + noun + ' is hidden by the filters.';
    var clear = document.createElement('button');
    clear.type = 'button';
    clear.className = 'filtered-out-clear';
    clear.textContent = 'Clear the filters and show it';
    clear.title = 'Clear every filter and go to this ' + noun;
    clear.addEventListener('click', function() {
        clear_all_filters();
        if (show) show(); else reveal_url_anchor(anchor);
    });
    note.appendChild(text);
    note.appendChild(clear);
    hidden.parentNode.insertBefore(note, hidden);
    // a feature's card, which the Features sort places by CSS order: the line takes the order just before it (5.2.5)
    if (hidden.parentNode.id === 'report-content') {
        note._card = hidden;
        if (hidden.style.order !== '') note.style.order = String(parseInt(hidden.style.order, 10) - 1);
    }
    // the sections round the line open, as they do round a scenario a link shows: in a closed feature it showed nothing
    for (var p = note.parentElement; p; p = p.parentElement) if (p.tagName === 'DETAILS') p.setAttribute('open', '');
    jump_into_view(note, 'center');
    return note;
}
// Opens and centres the scenario (or example row) an anchor names, and returns it: the one way to a scenario, for every
// link, the address and Next Failure's Back (5.2.0: a failure cluster's link had a way of its own, S16). With keepScroll
// it only opens what holds it, for a page about to be put back where the reader was (a reload, a return by Back).
function reveal_url_anchor(anchor, keepScroll) {
    drop_filtered_out_note();
    var el = anchor.indexOf('sid-') === 0
        ? element_for_stable_id(anchor.substring(4))
        : (document.getElementById(anchor) || document.querySelector('tr[data-scenario-id="' + anchor + '"]'));
    if (!el) return null;
    // Only the grouped copy of an outline row carries an `id`, so a slug anchor always lands there
    // even when the flat table is the one on display. Re-resolve through the stable id both rows
    // share so the row that gets selected is the row somebody can see.
    if (el.tagName === 'TR' && el.getAttribute('data-stable-id')) {
        el = element_for_stable_id(el.getAttribute('data-stable-id')) || el;
    }
    var hidden = filtered_out(el);
    if (hidden) return keepScroll ? null : filtered_out_note(hidden, anchor);
    // Open EVERY enclosing <details>, not only the feature: an example row lives inside a
    // parameterized group that starts collapsed, so opening the feature alone scrolls to nothing.
    var p = el;
    while (p) { if (p.tagName === 'DETAILS') p.setAttribute('open', ''); p = p.parentElement; }
    var target = el;
    if (el.tagName === 'TR') {
        el.click();
        target = el.closest('details.scenario-parameterized') || el;
    }
    // centred, or from its top when it is taller than the window below the bar (5.2.0: a failure cluster's link went to
    // the top, the rest centred, and a tall open scenario centred had its title above the window)
    var room = window.innerHeight - (parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--kron-nav-h')) || 0);
    if (!keepScroll) jump_into_view(target, target.getBoundingClientRect().height > room ? 'start' : 'center');
    return target;
}
function jump_into_view(target, block) {
    // At once, not smoothly. A feature or scenario has content-visibility: auto, so one the browser has
    // never drawn counts as its placeholder height (500 px for a closed feature about 60 px tall). A
    // smooth scroll is aimed once, across those placeholders, and they shrink to their true size as it
    // passes them: it ended past the target, at the bottom of the page. A jump draws only what it lands
    // among, but those shrink in the frames after it, which moves the target wherever the browser does
    // not anchor the scroll (Safari 26 and older have no scroll anchoring; Safari 27 turns it on), so the
    // jump is re-aimed while that settles.
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
// meaning, so a link copied from a 4.x report opens the same view (but for one with deps= and no depmode=: it meant
// AND, and now means OR, the filter's default): deps= is the chosen dependencies and depmode= their
// mode, hp=1 is Happy paths Only, and cats= and catmode=, a 4.x report's categories, are read as tags now that a
// category is a tag (plans/TAGS_PLAN.md §4.10). What 4.x could not say rides parameters of its own: depx= for the
// dependencies left out, tags= and tagx= for the tags chosen and left out, tagmode=, and hpx=1 for Happy paths Hide. The
// tags' names are percent-encoded one by one and joined with commas, so a comma inside a name stays in it. A chip for
// the scenarios with none is written by its reserved value (__none__, __untagged__), and a 4.x link's
// __uncategorized__ is read as __untagged__.
function url_hash_filters() {
    var parts = [];
    var search = document.getElementById('searchbar');
    if (search && search.value) parts.push('q=' + encodeURIComponent(search.value));
    var statuses = [];
    document.querySelectorAll('.status-toggle.status-active').forEach(function(b) { statuses.push(b.getAttribute('data-status')); });
    if (statuses.length > 0) parts.push('status=' + statuses.join(','));
    function list(key, values) { if (values.length > 0) parts.push(key + '=' + encodeURIComponent(values.join(','))); }
    function names(key, values) { if (values.length > 0) parts.push(key + '=' + values.map(encodeURIComponent).join(',')); }
    list('deps', chip_values('dependency', 'in'));
    list('depx', chip_values('dependency', 'out'));
    if (_depMode !== _depModeDefault) parts.push('depmode=' + _depMode);
    var happy = find_chip('tag', HAPPY_PATH_TAG), happyState = happy ? chip_state(happy) : 'off';
    if (happyState === 'in') parts.push('hp=1');
    if (happyState === 'out') parts.push('hpx=1');
    function notHappy(v) { return v !== HAPPY_PATH_TAG; }
    names('tags', chip_values('tag', 'in').filter(notHappy));
    names('tagx', chip_values('tag', 'out').filter(notHappy));
    if (_tagMode !== _tagModeDefault) parts.push('tagmode=' + _tagMode);
    var dur = document.getElementById('duration-threshold');
    if (dur && dur.value) parts.push('dur=' + dur.value);
    var activeP = document.querySelector('.percentile-btn.percentile-active');
    if (activeP) parts.push('pctl=' + encodeURIComponent(activeP.textContent));
    return parts;
}
// Every filter writes the address through here. The tab, the view and the anchor are the navigation script's (5.2.0): it
// keeps them in front, and decides whether the change is an entry of its own (plans/BACK_BUTTON_NAVIGATION_PLAN.md 4.8).
// Somebody who followed a link to one scenario and then narrowed the list still wants that link to be what they can
// copy back out, so the anchor stays.
function update_url_hash() {
    if (window.kronikol_nav) window.kronikol_nav.wrote(url_hash_filters().join('&'));
}
// Puts an address's filters on the page exactly: every filter is reset first, without writing the address and without
// closing a scenario, then the address's are applied, and the scenarios are shown or hidden once (5.2.0). Until 5.2.0
// this only ever turned on what an address named, so an address reached by Back, or typed into an open report, kept
// whatever the page had on besides (plans/BACK_BUTTON_NAVIGATION_PLAN.md S3).
function apply_url_hash(hash) {
    hash = String(hash || '').replace(/^#/, '');
    var anchor = url_anchor_of(hash);
    // Each parameter is read on its own, so one that does not decode, or a filter that fails on it, loses only itself and
    // the rest still apply (plans/TAGS_PLAN.md §4.10). raw keeps each value as written: the tags' names decode one by one.
    var params = {}, raw = {};
    hash.split('&').forEach(function(p) {
        var kv = p.split('=');
        if (kv.length !== 2) return;
        raw[kv[0]] = kv[1];
        try { params[kv[0]] = decodeURIComponent(kv[1]); } catch (e) { }
    });
    function step(apply) { try { apply(); } catch (e) { } }
    function split(value) { return value === undefined ? undefined : value.split(','); }
    // the tags' names, each decoded on its own; one that does not decode is left out
    function tagNames(value) {
        if (value === undefined) return undefined;
        return value.split(',').map(function(v) { try { return decodeURIComponent(v); } catch (e) { return null; } })
            .filter(function(v) { return v !== null && v !== ''; });
    }
    hold_visibility();
    try {
        step(reset_filters);
        // reset_filters clears the flags only: the release below shows what they leave, whether or not the address names a
        // filter (an address with none, reached by Back, runs no filter function to do it)
        step(function() { applyVisibility(fc()); });
        step(function() {
            if (params.q) {
                var sb = document.getElementById('searchbar');
                if (sb) { sb.value = params.q; run_search_scenarios(); }
            }
        });
        step(function() {
            if (params.status) {
                params.status.split(',').forEach(function(s) {
                    document.querySelectorAll('.status-toggle').forEach(function(btn) {
                        if (btn.getAttribute('data-status') === s) set_status_button(btn, true);
                    });
                });
                filter_statuses();
            }
        });
        // Both values are accepted symmetrically: a deep link from an AND-default report must
        // force AND on an OR-default report and vice versa (configured defaults mean either
        // value can differ from this report's own start mode). A 4.x link's catmode= sets the tags' mode when it has no tagmode=.
        if (params.depmode === 'OR' || params.depmode === 'AND') {
            _depMode = params.depmode;
            set_mode_button(document.querySelector('.dep-mode-toggle'), _depMode);
        }
        var tagMode = params.tagmode !== undefined ? params.tagmode : params.catmode;
        if (tagMode === 'AND' || tagMode === 'OR') {
            _tagMode = tagMode;
            set_mode_button(document.querySelector('.tag-mode-toggle'), _tagMode);
        }
        // a 4.x report drew the participant of a diagram with no call as a dependency: a link that chose it means none, and a
        // 4.x link's Uncategorized chip is the Untagged one
        var chips = function(family, values, state) {
            if (values === undefined) return false;
            values.forEach(function(v) {
                var b = find_chip(family, v);
                if (!b && family === 'dependency' && v === '(no interactions)') b = find_chip(family, '__none__');
                if (!b && family === 'tag' && v === '__uncategorized__') b = find_chip(family, '__untagged__');
                if (b) set_chip_state(b, state);
            });
            return true;
        };
        step(function() {
            var deps = chips('dependency', split(params.deps), 'in') | chips('dependency', split(params.depx), 'out');
            if (deps || params.depmode) filter_dependencies();
        });
        step(function() {
            var tags = chips('tag', tagNames(raw.tags), 'in') | chips('tag', tagNames(raw.tagx), 'out') | chips('tag', split(params.cats), 'in');
            var happy = find_chip('tag', HAPPY_PATH_TAG);
            if (happy && (params.hp === '1' || params.hpx === '1')) { set_chip_state(happy, params.hp === '1' ? 'in' : 'out'); tags = true; }
            if (tags || tagMode) filter_tags();
        });
        if (params.pctl) {
            document.querySelectorAll('.percentile-btn').forEach(function(b) {
                if (b.textContent === params.pctl) {
                    set_percentile_button(b, true);
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
    } finally {
        release_visibility();
    }
    // Last, because a filter pass can collapse what the anchor has to open, and the scroll has to
    // land on the layout the filters produced rather than the one they replaced.
    if (anchor) reveal_url_anchor(anchor);
}
