// The filter rows' chips (5.0.0, plans/OPTION_C_PLAN.md S6, D8 to D12). A tag or dependency chip goes off, then has,
// which is its row's active class as in 4.x, and on a three-way row then has not, its row's exclude class, where
// Alt+click and right-click go straight to has not. A row's ANY / ALL switch combines the chips it requires (a chip it
// leaves out always means none of them), a chip with data-none stands for the scenarios with none, and the row's Clear
// shows while one of its chips is set. Happy paths is the Happy Path chip under another face: Show, Only or Hide. A
// category is one more tag (plans/TAGS_PLAN.md §4.8), and the Tags row's start mode is the report's TagFilterMode.
var _tagModeDefault = '__TAG_MODE_DEFAULT__';
var _tagMode = _tagModeDefault;
// A row is two-way, its chips on or off, unless it is three-way (5.10.0, plans/REPORT_UI_FIXES_PLAN.md I1): while its own
// switch says so (the reader's, which the link writes as tagstates= or depstates=), or while one of its chips leaves its
// scenarios out (a link's tagx=, depx= or hpx=1, Happy paths' Hide), so a red chip only ever shows on a three-way row.
var _tagWaysDefault = '__TAG_WAYS_DEFAULT__', _depWaysDefault = '__DEP_WAYS_DEFAULT__';
var _chipWays = { tag: _tagWaysDefault, dependency: _depWaysDefault };
var CHIP_WAYS_TOGGLES = { tag: 'tag-ways-toggle', dependency: 'dep-ways-toggle' };
// the generator's words (ReportGenerator.ChipClicksIn, TwoWaysTitle, ThreeWaysTitle)
var CHIP_CLICKS = {
    2: 'Click: only these. Click again: off.',
    3: 'Click: only these. Click again: leave them out. Once more: off. Alt+click or right-click leaves them out at once.'
};
var CHIP_WAYS_TITLES = {
    2: "Two-way chips: a click shows only a chip's scenarios, a second click shows every scenario again. Click for three-way chips, where the second click leaves the chip's scenarios out (red) and a third shows them again.",
    3: "Three-way chips: a click shows only a chip's scenarios, a second leaves them out (red), a third shows every scenario again. Click for two-way chips."
};
var FILTER_CHIP_ROWS = {
    tag: { active: 'tag-active', exclude: 'tag-exclude' },
    dependency: { active: 'dependency-active', exclude: 'dependency-exclude' }
};
var HAPPY_PATH_TAG = 'Happy Path';
function chip_family(b) {
    for (var f in FILTER_CHIP_ROWS) if (b.classList.contains(f + '-toggle')) return f;
    return null;
}
// The filter box, where every filter control lives, or the page where there is none: its controls are looked for inside it,
// not across the page, which on 2,000 scenarios took 3 to 5 ms a walk, and a search or a tab change asks a dozen times
// (plans/SEARCH_PLAN.md 4.12)
function filter_box_root() { return document.getElementById('filtering-box') || document; }
function filter_chips(family) { return Array.prototype.slice.call(filter_box_root().querySelectorAll('.' + family + '-toggle')); }
// A tag is one tag whatever the case of its ASCII letters, as ScenarioTags.Comparer reads tags, and its chip carries the
// first spelling met: until 5.2.7 a link that spelled it another way (a 4.x report's fast beside Fast) chose no chip
function ascii_fold(s) { return String(s).replace(/[A-Z]/g, function (c) { return c.toLowerCase(); }); }
function find_chip(family, value) {
    var chips = filter_chips(family);
    for (var i = 0; i < chips.length; i++) if (chips[i].getAttribute('data-' + family) === value) return chips[i];
    if (family !== 'tag' || value == null) return null;
    var folded = ascii_fold(value);
    for (var j = 0; j < chips.length; j++) if (ascii_fold(chips[j].getAttribute('data-tag')) === folded) return chips[j];
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
    // a set chip always shows, in a long Tags row that starts without it too
    if (s !== 'off' && b.hidden) { b.hidden = false; sync_tag_more(); }
}
// A long Tags row starts with the tags the most scenarios carry (plans/TAGS_PLAN.md §4.8, Q9). Its More chip shows the
// chips the row starts without and hides them again, all but those that are set; it counts the ones still hidden.
function sync_tag_more() {
    var more = document.querySelector('.filtering-box .tag-more');
    if (!more) return;
    var expanded = more.getAttribute('aria-expanded') === 'true';
    var hidden = filter_chips('tag').filter(function (b) { return b.hidden; }).length;
    var shown = more.getAttribute('data-shown');
    more.hidden = !expanded && hidden === 0;
    more.textContent = expanded ? 'Fewer' : hidden + ' more';
    more.title = expanded
        ? 'Show only the ' + shown + ' tags the most scenarios carry, and the chips that are set.'
        : 'Show the other ' + hidden + (hidden === 1 ? ' tag' : ' tags') + '. The row starts with the ' + shown + ' tags the most scenarios carry.';
}
function toggle_tag_more(more) {
    var expand = more.getAttribute('aria-expanded') !== 'true';
    more.setAttribute('aria-expanded', expand ? 'true' : 'false');
    filter_chips('tag').forEach(function (b) {
        if (b.hasAttribute('data-more')) b.hidden = !expand && chip_state(b) === 'off';
    });
    sync_tag_more();
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
// a scenario's tags, its feature's with its own and Happy Path for a happy path, each spelled as its chip is (tag_names
// reads data-tags)
function item_tags(d) {
    if (!d.tags) d.tags = new Set(tag_names(d.el));
    return d.tags;
}
function filter_tags() {
    var c = fc(), sets = chip_sets('tag');
    // Only asks for a happy path whatever the row's mode, as 4.x's Happy Paths Only did beside its categories: from 5.0.0
    // to 5.2.6 the Happy Path chip joined the row's ANY, and Only with another chip showed that chip's scenarios as well
    var happyOnly = sets.req.indexOf(HAPPY_PATH_TAG) >= 0;
    if (happyOnly) sets.req = sets.req.filter(function (n) { return n !== HAPPY_PATH_TAG; });
    for (var i = 0; i < c.items.length; i++) {
        var names = item_tags(c.items[i]);
        c.items[i].tag = !(chip_match(names, sets, _tagMode === 'AND') && (!happyOnly || names.has(HAPPY_PATH_TAG)));
    }
    sync_happy_paths();
    applyVisibility(c);
    sync_group_clears();
    update_url_hash();
}
function filter_row(family) {
    if (family === 'tag') filter_tags();
    else filter_dependencies();
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
    filter_box_root().querySelectorAll('.happy-seg').forEach(function (b) {
        b.setAttribute('aria-pressed', b.getAttribute('data-hp') === state ? 'true' : 'false');
    });
}
// A row's Clear shows while one of its chips is set, and its ways switch says the row's ways: both follow the chips
function sync_group_clears() {
    for (var f in FILTER_CHIP_ROWS) {
        var row = document.querySelector('.filtering-box .' + f + '-filters .group-clear');
        if (row) row.hidden = !filter_chips(f).some(function (b) { return chip_state(b) !== 'off'; });
    }
    sync_ways();
}
function row_ways(family) {
    if (_chipWays[family] === '3') return 3;
    return filter_chips(family).some(function (b) { return chip_state(b) === 'out'; }) ? 3 : 2;
}
// the switch says its row's ways, and the row's chips what a click does in them; a chip's title is rebuilt from its count
// (data-scenarios), never parsed
function sync_ways() {
    for (var family in CHIP_WAYS_TOGGLES) {
        var btn = document.querySelector('.filtering-box .' + CHIP_WAYS_TOGGLES[family]);
        if (!btn) continue;
        var ways = String(row_ways(family));
        if (btn.getAttribute('data-ways') === ways) continue;
        btn.setAttribute('data-ways', ways);
        btn.textContent = ways + '-WAY';
        btn.title = CHIP_WAYS_TITLES[ways];
        filter_chips(family).forEach(function (b) {
            var n = +b.getAttribute('data-scenarios');
            b.title = (n === 1 ? '1 scenario' : n + ' scenarios') + '. ' + CHIP_CLICKS[ways];
        });
    }
}
// The switch: a three-way row turns two-way, its own ways and every chip that leaves its scenarios out (Happy paths' Hide
// with them, back to Show); a two-way row turns three-way and keeps its chips as they are
function toggle_ways(family) {
    if (row_ways(family) === 3) {
        _chipWays[family] = '2';
        filter_chips(family).forEach(function (b) { if (chip_state(b) === 'out') set_chip_state(b, 'off'); });
    } else {
        _chipWays[family] = '3';
    }
    filter_row(family);
}
// a three-way row: off, has, has not, off, and toward has not (Alt+click, a right-click) has not then off; a two-way row:
// on and off, whatever the keys (5.10.0)
function cycle_chip(chip, toExclude) {
    var family = chip_family(chip), s = chip_state(chip), next;
    if (row_ways(family) === 2) next = s === 'off' ? 'in' : 'off';
    else next = toExclude ? (s === 'out' ? 'off' : 'out') : (s === 'off' ? 'in' : s === 'in' ? 'out' : 'off');
    set_chip_state(chip, next);
    filter_row(family);
}
(function () {
    var CHIPS = '.filtering-box .tag-toggle, .filtering-box .dependency-toggle';
    document.addEventListener('click', function (e) {
        var t = e.target;
        if (!t || !t.closest) return;
        var chip = t.closest(CHIPS);
        if (chip) { cycle_chip(chip, e.altKey); return; }
        var more = t.closest('.filtering-box .tag-more');
        if (more) { toggle_tag_more(more); return; }
        var seg = t.closest('.filtering-box .happy-seg');
        if (seg) {
            var happy = find_chip('tag', HAPPY_PATH_TAG);
            if (happy) { set_chip_state(happy, seg.getAttribute('data-hp')); filter_tags(); }
            return;
        }
        var ways = t.closest('.filtering-box .tag-ways-toggle, .filtering-box .dep-ways-toggle');
        if (ways) { toggle_ways(ways.classList.contains('tag-ways-toggle') ? 'tag' : 'dependency'); return; }
        var mode = t.closest('.filtering-box .tag-mode-toggle, .filtering-box .dep-mode-toggle');
        if (mode) {
            if (mode.classList.contains('tag-mode-toggle')) toggle_tag_mode(mode);
            else toggle_dep_mode(mode);
            return;
        }
        var clear = t.closest('.filtering-box .group-clear');
        if (clear) {
            var row = clear.closest('.tag-filters, .dependency-filters');
            var family = row.classList.contains('tag-filters') ? 'tag' : 'dependency';
            filter_chips(family).forEach(function (b) { set_chip_state(b, 'off'); });
            filter_row(family);
            // the first chip that shows: a long Tags row can start without its first
            var first = filter_chips(family).filter(function (b) { return !b.hidden; })[0];
            if (first) first.focus();
        }
    });
    // right-click on a chip of a three-way row is the chip's: has not, in place of the browser's menu; on a two-way row it
    // is the browser's own (5.10.0)
    document.addEventListener('contextmenu', function (e) {
        var chip = e.target && e.target.closest ? e.target.closest(CHIPS) : null;
        if (!chip || row_ways(chip_family(chip)) !== 3) return;
        e.preventDefault();
        cycle_chip(chip, true);
    });
})();
