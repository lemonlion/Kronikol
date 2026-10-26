// The filter box and the report bar that carries it (5.0.0, plans/OPTION_C_PLAN.md S7; plans/SEARCH_PLAN.md 4.7). The box
// is written at the top of the bar, hidden, with every row showing (F1, F2, F5): the bar's Filters, shown at every width,
// at rest and pinned (F3), opens and closes it there, and counts the filters on (F4, F6); Esc and the box's close close it
// too. Until SEARCH_PLAN the box sat above the bar with its rows folded under More filters, Filters showed only while the
// bar floated and took the box into it, and the bar coming to rest put it back. The bar (D24) copies what the filters leave
// showing and offers Clear All while anything is filtered; its own status element says it (F8). The box shows the applied
// search, with a clear of its own (F10). The Features panel says when no scenario is left. The bar says when it is pinned
// (D25), and Duration and Happy paths join the Status line when they fit whole, decided on their measured widths (D18).
(function () {
    var doc = document, root = doc.documentElement;
    var phone = window.matchMedia ? window.matchMedia('(max-width: 768px)') : { matches: false };
    function $(s, r) { return (r || doc).querySelector(s); }
    function $$(s, r) { return Array.prototype.slice.call((r || doc).querySelectorAll(s)); }
    var box, nav;
    var hooks = { paint: function () {}, sync: function () {} };
    var empty = null;

    // ---- the measured layout (D18): Duration beside Status when both runs fit whole, Happy paths when all three do ----
    var needs = null;
    // an element's width on one line at its natural size, measured on a hidden clone
    function naturalWidth(node, inlineLabel) {
        var clone = node.cloneNode(true);
        $$('[id]', clone).forEach(function (n) { n.removeAttribute('id'); });
        clone.style.cssText += ';position:absolute;visibility:hidden;left:-99999px;top:0;width:max-content;max-width:none;flex-wrap:nowrap;';
        var label = inlineLabel && $(inlineLabel, clone);
        if (label) { label.style.flexBasis = 'auto'; label.style.marginRight = '8px'; }
        node.parentNode.appendChild(clone);
        var w = clone.getBoundingClientRect().width;
        clone.remove();
        return w;
    }
    function measureNeeds() {
        var status = $('.filter-row > .status-filters'), dur = $('.filter-row > .duration-filters'), happy = $('.filter-row > .happy-filters');
        // hidden, every width reads 0 and everything would fit: measured once the box shows
        if (!status || !status.offsetParent || !dur) return;
        needs = { pair: naturalWidth(status) + 28 + naturalWidth(dur, '.duration-filters-label') };
        if (happy) needs.trio = needs.pair + 28 + naturalWidth(happy, '.happy-filters-label');
    }
    function relayout() {
        if (!needs) measureNeeds();
        var row = $('.filter-row');
        if (!row || !box || !needs) return;
        var cs = getComputedStyle(box), avail = box.clientWidth - parseFloat(cs.paddingLeft) - parseFloat(cs.paddingRight);
        row.classList.toggle('pair-fits', needs.pair <= avail);
        row.classList.toggle('trio-fits', !!needs.trio && needs.trio <= avail);
    }
    var queued = false;
    function queue() {
        if (queued) return;
        queued = true;
        requestAnimationFrame(function () { queued = false; relayout(); });
    }
    function remeasure() { needs = null; queue(); }

    // ---- the filters on, counted on Filters (F6), as More filters counted them until SEARCH_PLAN ----
    // each status, each tag or dependency chip set either way, and one for the duration threshold, a percentile or a
    // custom value: read from the controls, never from the readout
    function filtersOn() {
        var n = box.querySelectorAll('.status-toggle.status-active, .tag-toggle.tag-active, .tag-toggle.tag-exclude, '
            + '.dependency-toggle.dependency-active, .dependency-toggle.dependency-exclude').length;
        var dur = $('#duration-threshold', box);
        if ($('.percentile-btn.percentile-active', box) || (dur && dur.value)) n += 1;
        return n;
    }

    // ---- the applied search, shown in the box (F10) ----
    function boxSearch() {
        var line = $('.box-search', box), field = doc.getElementById('searchbar');
        if (!line || !field) return;
        var q = field.value.trim();
        line.hidden = !q;
        $('.box-search-query', line).textContent = q;
    }

    // ---- the bar (D24, D25) ----
    function bar() {
        var row = $('.nav-row', nav), tabs = row && $('.report-tabs', row), group = row && $('.nav-filters', row), mark = $('.nav-mark');
        var readout = $('.filter-readout', box);
        if (!row || !tabs || !group || !mark || !readout) return;
        var text = $('.nav-readout', group), long = $('.nav-readout-long', group), short = $('.nav-readout-short', group);
        var clear = $('.nav-clear', group), toggle = $('.nav-filters-toggle', group), closeBtn = $('.filters-close', box);
        var count = $('.filters-count', toggle), status = $('.nav-status', nav);
        function stick() { return parseFloat(getComputedStyle(nav).top) || 0; }
        function pinAt() { return mark.getBoundingClientRect().top + window.pageYOffset - stick(); }
        function isOpen() { return !box.hidden; }

        // a narrow bar shortens the readout, then drops it, before the tabs would have to scroll; Filters is always in
        // the row, so it is always counted (F3)
        function fit() {
            row.classList.remove('nav-compact', 'nav-minimal');
            if (phone.matches) return;
            var room = function () { return tabs.scrollWidth + group.offsetWidth + 24 <= row.clientWidth; };
            if (room()) return;
            row.classList.add('nav-compact');
            if (room()) return;
            row.classList.add('nav-minimal');
        }

        // When a filter changes the list, Chrome keeps the page anchored on the focused control, and with that control in
        // the pinned bar it scrolls the page to the top. So where the page was when a filter control was used decides,
        // once the result changes: at or past the list's start, the results from the first right under the bar; above
        // it, where the reader was, while the first results are in sight. Only for a filter used from the bar, or the box
        // in it while the bar is pinned; at rest the browser's handling is left as it was.
        var before = null, shown = readout.textContent;
        function note(e) { if (!e.target.closest('.nav-filters-toggle, .filters-close')) before = { y: window.pageYOffset, at: Date.now() }; }
        [nav, empty].forEach(function (n) { if (n) ['pointerdown', 'keydown', 'input'].forEach(function (t) { n.addEventListener(t, note, true); }); });
        // a search answers twice (the quick pass, then the deep one), so the position holds for both, until a scroll
        function forget() { before = null; }
        window.addEventListener('wheel', forget, { passive: true });
        window.addEventListener('touchmove', forget, { passive: true });
        doc.addEventListener('keydown', function (e) {
            if (!nav.contains(e.target) && /^(PageUp|PageDown|Home|End|ArrowUp|ArrowDown| )$/.test(e.key)) forget();
        });
        function settle() {
            var now = readout.textContent, changed = now !== shown;
            shown = now;
            if (!changed) return;
            var was = before && Date.now() - before.at < 3000 ? before.y : null;
            if (was === null) return;
            var y = pinAt();
            if (!isOpen() && was < y - 1) return;
            if (was >= y - 1) { window.scrollTo(0, y); return; }
            window.scrollTo(0, was);
            if (nav.getBoundingClientRect().bottom + 14 > window.innerHeight - 120) window.scrollTo(0, y);
        }

        // Filters' count, name and title (F4, F6): "Filters, 2 on" while two are on
        hooks.paint = function () {
            var open = isOpen(), n = filtersOn();
            toggle.setAttribute('aria-expanded', String(open));
            count.textContent = n ? String(n) : '';
            count.hidden = !n;
            toggle.classList.toggle('has-on', n > 0);
            toggle.setAttribute('aria-label', n ? 'Filters, ' + n + ' on' : 'Filters');
            toggle.title = open ? 'Hide the filters' : n ? 'Show the filters: ' + (n === 1 ? 'one is on' : n + ' are on') : 'Show the filters';
        };
        // the count follows every change of the filters: a control used, Clear All, a filter read from the link
        ['click', 'input', 'change', 'keyup'].forEach(function (t) { doc.addEventListener(t, function () { setTimeout(hooks.paint, 0); }, true); });

        // the copy follows the box's readout: the whole line, and a short form for a narrow bar; the status element says it
        hooks.sync = function () {
            var filtered = readout.classList.contains('is-filtered'), now = readout.textContent;
            long.innerHTML = readout.innerHTML;
            var m = /(\d+) of (\d+) scenarios?/.exec(now), all = /All (\d+ scenarios?)/.exec(now);
            short.textContent = '';
            if (filtered && m) {
                var b = doc.createElement('b');
                b.textContent = 'Filtered';
                short.appendChild(b);
                short.appendChild(doc.createTextNode(' ' + m[1] + ' of ' + m[2]));
            } else short.textContent = all ? 'All ' + all[1] : now;
            var said = now.replace(/\s+/g, ' ').trim();
            if (status && status.textContent !== said) status.textContent = said;
            text.classList.toggle('is-filtered', filtered);
            toggle.classList.toggle('is-active', filtered);
            clear.hidden = !filtered;
            fit();
            settle();
        };
        clear.addEventListener('click', function () { clear_all_filters(); });

        // what was being read just below a pinned bar stays just below it when the bar changes height
        function reading() {
            if (window.pageYOffset < pinAt() - 0.5) return null;
            var r = nav.getBoundingClientRect();
            var e = doc.elementFromPoint(Math.round(r.left + r.width / 2), Math.round(r.bottom + 8));
            if (!e || e === root || e === doc.body || nav.contains(e)) return null;
            return { e: e, off: e.getBoundingClientRect().top - r.bottom };
        }
        // A scroll can pin or unpin the bar, which moves its bottom edge, so one correction may land short: corrected
        // again until it holds.
        function keep(k) {
            if (!k || !k.e.isConnected) return;
            for (var i = 0; i < 3; i++) {
                var d = k.e.getBoundingClientRect().top - nav.getBoundingClientRect().bottom - k.off;
                if (Math.abs(d) <= 0.5) return;
                window.scrollBy(0, d);
            }
        }
        // the browser's own scroll anchoring would move the page as well: off while the box opens or closes
        function moving(fn) {
            root.style.overflowAnchor = 'none';
            try { fn(); } finally { requestAnimationFrame(function () { root.style.overflowAnchor = ''; }); }
        }
        // pinned, the box takes at most what leaves the bar 60% of the window, and scrolls inside past that; at rest it
        // is in the page's flow and takes what it needs
        var floating = null;
        function capBox() {
            box.style.maxHeight = '';
            if (!isOpen() || !floating) return;
            var rows = nav.getBoundingClientRect().height - box.getBoundingClientRect().height;
            var cap = Math.max(180, Math.round(window.innerHeight * 0.6 - rows));
            // the cap is the box's whole height: a max-height of the content box leaves the padding and border on top of it,
            // which until SEARCH_PLAN took a pinned bar past 60% of the window by the box's padding
            var cs = getComputedStyle(box), extra = cs.boxSizing === 'border-box' ? 0
                : parseFloat(cs.paddingTop) + parseFloat(cs.paddingBottom) + parseFloat(cs.borderTopWidth) + parseFloat(cs.borderBottomWidth);
            if (box.getBoundingClientRect().height > cap + 1) box.style.maxHeight = (cap - extra) + 'px';
        }
        function setOpen(open, focus) {
            if (open === isOpen()) return;
            var k = reading();
            moving(function () {
                box.hidden = !open;
                nav.classList.toggle('has-filters', open);
                // while the box shows, its own header carries the readout and Clear All
                group.classList.toggle('riding', open);
                if (open) relayout();
                capBox();
                hooks.paint();
                keep(k);
                // opened near the top with the bar low in a short window: scroll until the box shows, or the bar pins
                if (open && !k) {
                    var nr = nav.getBoundingClientRect(), need = box.getBoundingClientRect().bottom + 8 - window.innerHeight;
                    if (need > 0 && nr.top > stick()) window.scrollBy(0, Math.min(need, nr.top - stick()));
                }
            });
            // opened, the focus goes to the box, so the next Tab reaches its first control; closed, back to Filters (F4)
            if (open && focus) box.focus({ preventScroll: true });
            else if (!open && focus) toggle.focus({ preventScroll: true });
        }
        toggle.addEventListener('click', function () { setOpen(!isOpen(), true); });
        closeBtn.addEventListener('click', function () { setOpen(false, true); });
        function onEscape(e) {
            if (e.key !== 'Escape' || !isOpen()) return;
            e.preventDefault();
            setOpen(false, true);
        }
        box.addEventListener('keydown', onEscape);
        group.addEventListener('keydown', onEscape);
        $$('.report-tab', nav).forEach(function (t) { t.addEventListener('click', function () { setTimeout(fit, 0); }); });

        // Pinned, the bar squares its corners and casts a shadow (D25); the box stays open as the bar comes to rest (F2),
        // and is capped only while the bar floats.
        function scrolled() {
            var at = mark.getBoundingClientRect().top, line = stick(), sticky = getComputedStyle(nav).position === 'sticky';
            nav.classList.toggle('is-pinned', sticky && at < line - 0.5);
            var f = sticky && at <= line + 0.5;
            if (f === floating) return;
            floating = f;
            nav.classList.toggle('k-floating', f);
            capBox();
        }
        var ticking = false;
        window.addEventListener('scroll', function () {
            if (ticking) return;
            ticking = true;
            requestAnimationFrame(function () { ticking = false; scrolled(); });
        }, { passive: true });
        window.addEventListener('resize', function () { fit(); capBox(); scrolled(); });
        hooks.sync();
        hooks.paint();
        scrolled();
    }

    // ---- the empty state ----
    // the Features panel says when the filters leave no scenario
    // "No scenarios match" only while nothing shows: the readout counts tests, and a scenario that is not a test (a
    // stack's start-up) can show while it reads 0 of them (data-none-shown, report-scenario-feature-map-helper.js)
    function emptyState() {
        var readout = empty && $('.filter-readout', box);
        if (!readout) return;
        empty.hidden = !(readout.classList.contains('is-filtered') && readout.hasAttribute('data-none-shown'));
    }
    // the way back from a cleared filter is the search: its button, where a reader starts again (SEARCH_PLAN 4.1), else
    // the field while the search has one in the page
    function focusSearch() {
        var to = doc.getElementById('search-button') || doc.getElementById('searchbar');
        if (to && to.getClientRects().length) to.focus({ preventScroll: true });
    }

    function start() {
        box = $('.filtering-box');
        if (!box) return;
        nav = $('.toolbar-row.report-nav');
        empty = $('.filters-empty');
        if (empty) $('.filters-empty-clear', empty).addEventListener('click', function () {
            clear_all_filters();
            // the empty state goes with the filters it reported, and focus with it: to the search
            focusSearch();
        });
        // the note a tab that follows the filters writes while they leave scenarios out ends with the same way back
        // (5.1.0), and focus goes the same way, the note going with the filters it reported
        doc.addEventListener('click', function (e) {
            var clear = e.target && e.target.closest ? e.target.closest('.filtered-note-clear') : null;
            if (!clear) return;
            clear_all_filters();
            focusSearch();
        });
        // the box's clear of the search clears the search alone: one change of one control, one entry (F10)
        var searchClear = $('.box-search-clear', box);
        if (searchClear) searchClear.addEventListener('click', function () {
            var field = doc.getElementById('searchbar');
            if (!field) return;
            field.value = '';
            run_search_scenarios();
            var f = $('.status-toggle', box);
            if (f) f.focus({ preventScroll: true });
        });
        // a link's filters were read before this started
        emptyState();
        boxSearch();
        if (nav) bar();
        relayout();
        window.addEventListener('resize', remeasure);
        doc.addEventListener('toggle', queue, true);
        doc.addEventListener('click', queue, true);
        var status = $('.filter-row > .status-filters');
        if (window.ResizeObserver) {
            // a reader's text spacing or a font landing late changes what the runs need; the box's width what there is
            var observer = new ResizeObserver(remeasure);
            if (status) observer.observe(status);
            observer.observe(box);
        }
        if (doc.fonts && doc.fonts.ready) doc.fonts.ready.then(remeasure);
    }
    // what the filters leave showing changes the empty state, the bar's copy, the count and the box's search line
    doc.addEventListener('kron-filter-change', function () { if (!box) return; emptyState(); boxSearch(); hooks.sync(); hooks.paint(); });
    // after the report's own start, which reads the filters from the link
    if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', function () { setTimeout(start, 0); });
    else setTimeout(start, 0);
})();
