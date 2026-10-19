// The filter rows' chips (5.0.0, plans/OPTION_C_PLAN.md S6, D8 to D12). A tag, dependency or category chip goes off,
// then has, which is its row's active class as in 4.x, then has not, its row's exclude class; Alt+click and right-click
// go straight to has not. A row's ANY / ALL switch combines the chips it requires (a chip it leaves out always means
// none of them), a chip with data-none stands for the scenarios with none, and the row's Clear shows while one of its
// chips is set. Happy paths is the Happy Path chip under another face: Show, Only or Hide.
var _tagModeDefault = 'OR';
var _tagMode = _tagModeDefault;
var FILTER_CHIP_ROWS = {
    tag: { active: 'tag-active', exclude: 'tag-exclude' },
    dependency: { active: 'dependency-active', exclude: 'dependency-exclude' },
    category: { active: 'category-active', exclude: 'category-exclude' }
};
var HAPPY_PATH_TAG = 'Happy Path';
function chip_family(b) {
    for (var f in FILTER_CHIP_ROWS) if (b.classList.contains(f + '-toggle')) return f;
    return null;
}
function filter_chips(family) { return Array.prototype.slice.call(document.querySelectorAll('.filtering-box .' + family + '-toggle')); }
function find_chip(family, value) {
    var chips = filter_chips(family);
    for (var i = 0; i < chips.length; i++) if (chips[i].getAttribute('data-' + family) === value) return chips[i];
    return null;
}
function chip_state(b) {
    var f = FILTER_CHIP_ROWS[chip_family(b)];
    return b.classList.contains(f.active) ? 'in' : b.classList.contains(f.exclude) ? 'out' : 'off';
}
function set_chip_state(b, s) {
    var f = FILTER_CHIP_ROWS[chip_family(b)];
    b.classList.toggle(f.active, s === 'in');
    b.classList.toggle(f.exclude, s === 'out');
    b.setAttribute('aria-pressed', s === 'in' ? 'true' : s === 'out' ? 'mixed' : 'false');
}
// the values of a row's chips in a state, as the link writes them (a chip for none by its reserved value)
function chip_values(family, state) {
    return filter_chips(family).filter(function (b) { return chip_state(b) === state; }).map(function (b) { return b.getAttribute('data-' + family); });
}
// what a row asks of a scenario: the names it requires and leaves out, and whether it asks for or against having none
function chip_sets(family) {
    var sets = { req: [], exc: [], reqNone: false, excNone: false };
    filter_chips(family).forEach(function (b) {
        var s = chip_state(b), none = b.hasAttribute('data-none'), v = b.getAttribute('data-' + family);
        if (s === 'in') { if (none) sets.reqNone = true; else sets.req.push(v); }
        else if (s === 'out') { if (none) sets.excNone = true; else sets.exc.push(v); }
    });
    return sets;
}
function chip_match(names, sets, all) {
    var none = names.size === 0;
    if (sets.req.length || sets.reqNone) {
        var hits = sets.req.map(function (n) { return names.has(n); });
        if (sets.reqNone) hits.push(none);
        if (all ? !hits.every(Boolean) : !hits.some(Boolean)) return false;
    }
    if (sets.excNone && none) return false;
    for (var i = 0; i < sets.exc.length; i++) if (names.has(sets.exc[i])) return false;
    return true;
}
function names_of(value) {
    return new Set(value ? value.split(',').map(function (s) { return s.trim(); }).filter(Boolean) : []);
}
function item_tags(d) {
    if (!d.tags) {
        d.tags = names_of(d.el.getAttribute('data-labels') || '');
        if (d.isHappy) d.tags.add(HAPPY_PATH_TAG);
    }
    return d.tags;
}
function item_categories(d) {
    if (!d.cats) d.cats = names_of(d.el.getAttribute('data-categories') || '');
    return d.cats;
}
function filter_tags() {
    var c = fc(), sets = chip_sets('tag');
    for (var i = 0; i < c.items.length; i++) c.items[i].tag = !chip_match(item_tags(c.items[i]), sets, _tagMode === 'AND');
    sync_happy_paths();
    applyVisibility(c);
    sync_group_clears();
    update_url_hash();
}
function filter_row(family) {
    if (family === 'tag') filter_tags();
    else if (family === 'dependency') filter_dependencies();
    else filter_categories();
}
// a row's switch says ANY or ALL; the mode underneath stays OR or AND, which the link writes
function set_mode_button(btn, mode) {
    if (!btn) return;
    btn.setAttribute('data-mode', mode);
    btn.textContent = mode === 'AND' ? 'ALL' : 'ANY';
}
function toggle_tag_mode(btn) {
    _tagMode = _tagMode === 'AND' ? 'OR' : 'AND';
    set_mode_button(btn, _tagMode);
    filter_tags();
}
// Happy paths shows the Happy Path chip's state
function sync_happy_paths() {
    var chip = find_chip('tag', HAPPY_PATH_TAG), state = chip ? chip_state(chip) : 'off';
    document.querySelectorAll('.filtering-box .happy-seg').forEach(function (b) {
        b.setAttribute('aria-pressed', b.getAttribute('data-hp') === state ? 'true' : 'false');
    });
}
function sync_group_clears() {
    for (var f in FILTER_CHIP_ROWS) {
        var row = document.querySelector('.filtering-box .' + f + '-filters .group-clear');
        if (row) row.hidden = !filter_chips(f).some(function (b) { return chip_state(b) !== 'off'; });
    }
}
// off, has, has not, off; toward has not, has not then off
function cycle_chip(chip, toExclude) {
    var s = chip_state(chip);
    var next = toExclude ? (s === 'out' ? 'off' : 'out') : (s === 'off' ? 'in' : s === 'in' ? 'out' : 'off');
    set_chip_state(chip, next);
    filter_row(chip_family(chip));
}
(function () {
    var CHIPS = '.filtering-box .tag-toggle, .filtering-box .dependency-toggle, .filtering-box .category-toggle';
    document.addEventListener('click', function (e) {
        var t = e.target;
        if (!t || !t.closest) return;
        var chip = t.closest(CHIPS);
        if (chip) { cycle_chip(chip, e.altKey); return; }
        var seg = t.closest('.filtering-box .happy-seg');
        if (seg) {
            var happy = find_chip('tag', HAPPY_PATH_TAG);
            if (happy) { set_chip_state(happy, seg.getAttribute('data-hp')); filter_tags(); }
            return;
        }
        var mode = t.closest('.filtering-box .tag-mode-toggle, .filtering-box .dep-mode-toggle, .filtering-box .cat-mode-toggle');
        if (mode) {
            if (mode.classList.contains('tag-mode-toggle')) toggle_tag_mode(mode);
            else if (mode.classList.contains('dep-mode-toggle')) toggle_dep_mode(mode);
            else toggle_cat_mode(mode);
            return;
        }
        var clear = t.closest('.filtering-box .group-clear');
        if (clear) {
            var row = clear.closest('.tag-filters, .dependency-filters, .category-filters');
            var family = row.classList.contains('tag-filters') ? 'tag' : row.classList.contains('dependency-filters') ? 'dependency' : 'category';
            filter_chips(family).forEach(function (b) { set_chip_state(b, 'off'); });
            filter_row(family);
            var first = filter_chips(family)[0];
            if (first) first.focus();
        }
    });
    // right-click on a chip is the chip's: has not, in place of the browser's menu
    document.addEventListener('contextmenu', function (e) {
        var chip = e.target && e.target.closest ? e.target.closest(CHIPS) : null;
        if (!chip) return;
        e.preventDefault();
        cycle_chip(chip, true);
    });
})();
