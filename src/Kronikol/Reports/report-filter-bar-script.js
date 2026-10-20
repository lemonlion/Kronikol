// The filter box under More filters, and the report bar carrying the filters (5.0.0, plans/OPTION_C_PLAN.md S7).
// More filters (D15) opens and folds the rows under the search line on a desktop, and while they are folded counts the
// filters on in them. The bar (D24) copies what the filters leave showing, offers Clear All while anything is filtered,
// and while it floats has Filters, which takes the box into the bar: there it pins with the bar and covers nothing, and
// Esc, its close, a phone's width or the bar coming to rest puts it back. The Features panel says when no scenario is
// left. The bar says when it is pinned (D25), and Duration and Happy paths join the Status line when they fit whole,
// decided on their measured widths (D18). On a phone the bar scrolls away and the box keeps its own Filters button, which
// folds the rows with More filters' class, so they stay as they were across the phone breakpoint, and whose line the
// readout rides (5.0.0, S12).
(function () {
    var doc = document, root = doc.documentElement;
    var phone = window.matchMedia ? window.matchMedia('(max-width: 768px)') : { matches: false };
    function $(s, r) { return (r || doc).querySelector(s); }
    function $$(s, r) { return Array.prototype.slice.call((r || doc).querySelectorAll(s)); }
    var box, nav, riding = false;
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
        // folded, every width reads 0 and everything would fit: measured once the rows show
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

    // ---- More filters (D15) ----
    function moreFilters() {
        var more = $('.more-filters', box), count = more && $('.more-filters-count', more), phoneToggle = $('.mobile-filter-toggle', box);
        if (!more) return;
        var idle = more.getAttribute('title');
        // the filters on in the rows: each status, each tag or dependency chip set either way, and one for the
        // duration threshold, a percentile or a custom value
        function on() {
            var n = box.querySelectorAll('.status-toggle.status-active, .tag-toggle.tag-active, .tag-toggle.tag-exclude, '
                + '.dependency-toggle.dependency-active, .dependency-toggle.dependency-exclude').length;
            var dur = $('#duration-threshold', box);
            if ($('.percentile-btn.percentile-active', box) || (dur && dur.value)) n += 1;
            return n;
        }
        hooks.paint = function () {
            var open = !box.classList.contains('rows-folded'), n = on();
            more.setAttribute('aria-expanded', String(open));
            if (phoneToggle) phoneToggle.setAttribute('aria-expanded', String(open));
            count.textContent = n ? String(n) : '';
            count.hidden = open || !n;
            more.classList.toggle('has-on', !open && n > 0);
            more.setAttribute('aria-label', !open && n ? 'More filters, ' + n + ' on' : 'More filters');
            more.title = open ? 'Fold the filters away' : n ? (n === 1 ? 'One filter here is on' : n + ' filters here are on') + ': show them' : idle;
        };
        // the count follows every change of the filters: a control used, Clear All, a filter read from the link
        ['click', 'input', 'change', 'keyup'].forEach(function (t) { doc.addEventListener(t, function () { setTimeout(hooks.paint, 0); }, true); });
        hooks.paint();
    }

    // ---- the bar (D24, D25) ----
    // in the bar the box changes its height with its rows: what is below the bar stays put
    var reflow = function (fn) { fn(); };
    function bar() {
        var row = $('.nav-row', nav), tabs = row && $('.report-tabs', row), group = row && $('.nav-filters', row), mark = $('.nav-mark');
        var readout = $('.filter-readout', box);
        if (!row || !tabs || !group || !mark || !readout) return;
        var text = $('.nav-readout', group), long = $('.nav-readout-long', group), short = $('.nav-readout-short', group);
        var clear = $('.nav-clear', group), toggle = $('.nav-filters-toggle', group), closeBtn = $('.filters-close', box);
        var home = { parent: box.parentNode, next: box.nextSibling };
        function stick() { return parseFloat(getComputedStyle(nav).top) || 0; }
        function pinAt() { return mark.getBoundingClientRect().top + window.pageYOffset - stick(); }

        // a narrow bar shortens the readout, then drops it, before the tabs would have to scroll
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
        // it, where the reader was, while the first results are in sight. Only for a filter used from the bar, or with the
        // box in it; with the box in its own place the browser's handling is left as it was.
        var before = null, shown = readout.textContent;
        function note(e) { if (!e.target.closest('.nav-filters-toggle, .filters-close')) before = { y: window.pageYOffset, at: Date.now() }; }
        [nav, box, empty].forEach(function (n) { if (n) ['pointerdown', 'keydown', 'input'].forEach(function (t) { n.addEventListener(t, note, true); }); });
        // a search answers twice (the quick pass, then the deep one), so the position holds for both, until a scroll
        function forget() { before = null; }
        window.addEventListener('wheel', forget, { passive: true });
        window.addEventListener('touchmove', forget, { passive: true });
        doc.addEventListener('keydown', function (e) {
            if (!nav.contains(e.target) && !box.contains(e.target) && /^(PageUp|PageDown|Home|End|ArrowUp|ArrowDown| )$/.test(e.key)) forget();
        });
        function settle() {
            var now = readout.textContent, changed = now !== shown;
            shown = now;
            if (!changed) return;
            var was = before && Date.now() - before.at < 3000 ? before.y : null;
            if (was === null) return;
            var y = pinAt();
            if (!riding && was < y - 1) return;
            if (was >= y - 1) { window.scrollTo(0, y); return; }
            window.scrollTo(0, was);
            if (nav.getBoundingClientRect().bottom + 14 > window.innerHeight - 120) window.scrollTo(0, y);
        }

        // the copy follows the box's readout: the whole line, and a short form for a narrow bar
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
        // A scroll can pin or unpin the bar, which moves its bottom edge, so one correction may land short (the preview's
        // did: closing put the bar back at rest, the box above it in view): corrected again until it holds.
        function keep(k) {
            if (!k || !k.e.isConnected) return;
            for (var i = 0; i < 3; i++) {
                var d = k.e.getBoundingClientRect().top - nav.getBoundingClientRect().bottom - k.off;
                if (Math.abs(d) <= 0.5) return;
                window.scrollBy(0, d);
            }
        }
        // the browser's own scroll anchoring would move the page as well: off while the box moves
        function moving(fn) {
            root.style.overflowAnchor = 'none';
            try { fn(); } finally { requestAnimationFrame(function () { root.style.overflowAnchor = ''; }); }
        }
        // in the bar the box takes at most what leaves the bar 60% of the window, and scrolls inside past that
        function capBox() {
            box.style.maxHeight = '';
            if (!riding) return;
            var rows = nav.getBoundingClientRect().height - box.getBoundingClientRect().height;
            var cap = Math.max(180, Math.round(window.innerHeight * 0.6 - rows));
            if (box.scrollHeight > cap + 1) box.style.maxHeight = cap + 'px';
        }
        function open() {
            if (riding || phone.matches) return;
            var k = reading();
            moving(function () {
                // at the top of the bar, where the box sits above the bar at rest
                nav.insertBefore(box, nav.firstChild);
                riding = true;
                nav.classList.add('has-filters');
                group.classList.add('riding');
                closeBtn.hidden = false;
                toggle.setAttribute('aria-expanded', 'true');
                toggle.title = 'Put the filter box back above the bar';
                relayout();
                capBox();
                keep(k);
                // opened near the top with the bar low in a short window: scroll until the box shows, or the bar pins
                if (!k) {
                    var nr = nav.getBoundingClientRect(), need = box.getBoundingClientRect().bottom + 8 - window.innerHeight;
                    if (need > 0 && nr.top > stick()) window.scrollBy(0, Math.min(need, nr.top - stick()));
                }
            });
            var first = $('#searchbar', box) || $('input, button', box);
            if (first) first.focus({ preventScroll: true });
        }
        function shut(refocus) {
            if (!riding) return;
            var k = reading();
            moving(function () {
                home.parent.insertBefore(box, home.next);
                riding = false;
                box.style.maxHeight = '';
                nav.classList.remove('has-filters');
                group.classList.remove('riding');
                closeBtn.hidden = true;
                toggle.setAttribute('aria-expanded', 'false');
                toggle.title = 'Show the filter box in this bar, so it stays in view as you scroll';
                relayout();
                keep(k);
            });
            if (refocus) toggle.focus({ preventScroll: true });
        }
        reflow = function (fn) {
            if (!riding) { fn(); return; }
            var k = reading();
            moving(function () { fn(); capBox(); keep(k); });
        };
        toggle.addEventListener('click', function () { if (riding) shut(false); else open(); });
        closeBtn.addEventListener('click', function () { shut(true); });
        function onEscape(e) { if (e.key === 'Escape' && riding) { e.preventDefault(); shut(true); } }
        box.addEventListener('keydown', onEscape);
        group.addEventListener('keydown', onEscape);
        $$('.report-tab', nav).forEach(function (t) { t.addEventListener('click', function () { setTimeout(fit, 0); }); });

        // Filters shows only while the bar floats, at the pin line included (a filter change scrolls the results to start
        // right there); once the bar comes to rest, a box riding in it goes back above it. Pinned, the bar squares its
        // corners and casts a shadow (D25).
        var floating = null;
        function scrolled() {
            var at = mark.getBoundingClientRect().top, line = stick(), sticky = getComputedStyle(nav).position === 'sticky';
            nav.classList.toggle('is-pinned', sticky && at < line - 0.5);
            var f = !phone.matches && at <= line + 0.5;
            if (f === floating) return;
            floating = f;
            nav.classList.toggle('k-floating', f);
            if (!f && riding) shut(!!doc.activeElement && box.contains(doc.activeElement));
        }
        var ticking = false;
        window.addEventListener('scroll', function () {
            if (ticking) return;
            ticking = true;
            requestAnimationFrame(function () { ticking = false; scrolled(); });
        }, { passive: true });
        window.addEventListener('resize', function () { if (phone.matches) shut(false); fit(); capBox(); scrolled(); });
        hooks.sync();
        scrolled();
    }

    // ---- the phone's Filters line ----
    // On a phone the rows fold under the Filters button, whose line is otherwise empty: the readout rides it there, so it
    // costs no height (the preview's c-dom.r5.js), and goes back before Clear All on a wider window. The element itself
    // moves, so the filters, which write it, and the bar, which copies it, keep it.
    function placeReadout() {
        var readout = $('.filter-readout', box), line = $('.mobile-filter-line', box);
        var clear = $('.filtering-box-export .export-btn[onclick="clear_all_filters()"]', box);
        if (!readout || !line || !clear) return;
        if (phone.matches) { if (readout.parentNode !== line) line.appendChild(readout); }
        else if (readout.nextSibling !== clear) clear.parentNode.insertBefore(readout, clear);
    }

    // ---- the empty state ----
    // the Features panel says when the filters leave no scenario, on a page with the bar and on one without it (a page
    // with nothing beside its features has no tabs, and so no bar)
    function emptyState() {
        var readout = empty && $('.filter-readout', box);
        if (!readout) return;
        var m = /(\d+) of (\d+) scenarios?/.exec(readout.textContent);
        empty.hidden = !(readout.classList.contains('is-filtered') && m && m[1] === '0');
    }

    function start() {
        box = $('.filtering-box');
        if (!box) return;
        placeReadout();
        if (phone.addEventListener) phone.addEventListener('change', placeReadout);
        else if (phone.addListener) phone.addListener(placeReadout);
        nav = $('.toolbar-row.report-nav');
        empty = $('.filters-empty');
        if (empty) $('.filters-empty-clear', empty).addEventListener('click', function () {
            clear_all_filters();
            // the empty state goes with the filters it reported, and focus with it: to the search, where a reader starts again
            var search = doc.getElementById('searchbar');
            if (search && search.getClientRects().length) search.focus({ preventScroll: true });
        });
        // the note a tab that follows the filters writes while they leave scenarios out ends with the same way back
        // (5.1.0), and focus goes the same way, the note going with the filters it reported
        doc.addEventListener('click', function (e) {
            var clear = e.target && e.target.closest ? e.target.closest('.filtered-note-clear') : null;
            if (!clear) return;
            clear_all_filters();
            var search = doc.getElementById('searchbar');
            if (search && search.getClientRects().length) search.focus({ preventScroll: true });
        });
        // a link's filters were read before this started
        emptyState();
        if (nav) bar();
        moreFilters();
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
    // what the filters leave showing changes the empty state, the bar's copy and the count
    doc.addEventListener('kron-filter-change', function () { emptyState(); hooks.sync(); hooks.paint(); });
    // More filters, and on a phone the Filters button, answer from the moment the page has them, before the rest of this
    // starts: both set the one fold class
    doc.addEventListener('click', function (e) {
        var more = e.target && e.target.closest ? e.target.closest('.filtering-box .more-filters, .filtering-box .mobile-filter-toggle') : null;
        if (!more) return;
        var b = more.closest('.filtering-box'), open = b.classList.contains('rows-folded');
        reflow(function () { b.classList.toggle('rows-folded', !open); hooks.paint(); });
        queue();
    });
    // after the report's own start, which reads the filters from the link
    if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', function () { setTimeout(start, 0); });
    else setTimeout(start, 0);
})();
