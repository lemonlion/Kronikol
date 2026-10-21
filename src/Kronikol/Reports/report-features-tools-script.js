// The Features tab's tools (5.0.0, plans/OPTION_C_PLAN.md S4, D21, D22, D35): the Full and Summary views, the sort, a
// table row opening its feature, expand and collapse for features and for scenarios, and the phone's display settings
// gear. The generator writes every control where it stays and in the state the page opens in; this script answers them.
(function () {
    var FOLDS = { features: 'details.feature', scenarios: 'details.scenario' };

    function viewSwitch() { return document.querySelector('.view-switch'); }

    // Full shows the feature cards, Summary the features table; the bar is told which, so the tools for the cards hide
    function setView(view, focus) {
        var sw = viewSwitch();
        if (!sw) return;
        var summary = view === 'summary';
        sw.querySelectorAll('.view-btn').forEach(function (tab) {
            var on = tab.getAttribute('data-view') === (summary ? 'summary' : 'full');
            tab.setAttribute('aria-selected', on ? 'true' : 'false');
            tab.tabIndex = on ? 0 : -1;
            if (on && focus) tab.focus();
        });
        var table = document.getElementById('features-table-view'), cards = document.getElementById('report-content');
        if (!table || !cards || cards.hidden === summary) return;
        table.hidden = !summary;
        cards.hidden = summary;
        // the failure clusters go with the cards: the Summary view is the features table alone (5.1.0)
        var clusters = document.querySelector('.failure-clusters');
        if (clusters) clusters.hidden = summary;
        var bar = sw.closest('.toolbar-row');
        if (bar) bar.classList.toggle('features-table-mode', summary);
        // a diagram the cards hid while the page opened on the table is drawn once it shows, and only one that shows
        if (!summary && typeof window._renderDiagramsInContainer === 'function') window._renderDiagramsInContainer(cards, true);
        window.dispatchEvent(new Event('resize'));
    }
    window.kronikol_set_features_view = function (view) { setView(view); };
    // a view chosen by the reader is a place (5.2.0, plans/BACK_BUTTON_NAVIGATION_PLAN.md Q14): Back returns to the one
    // before; run names a run of arrow keys, which is one entry
    function go(view, run) {
        if (window.kronikol_nav) window.kronikol_nav.go({ view: view }, run ? { key: run } : null);
        else setView(view);
    }

    // Each row of the table beside its card, paired once as the page opens: both are written in the same order, and a
    // click on a column heading reorders the rows later. A pair is found by position, never by name, so two features
    // with one name keep a row each.
    var pairs = null;
    function pair() {
        var cards = document.getElementById('report-content');
        var body = document.querySelector('#features-table-view .feature-summary-table tbody');
        if (!cards || !body) return;
        var list = cards.querySelectorAll(':scope > details.feature'), rows = body.rows;
        if (list.length !== rows.length) return;
        pairs = [];
        for (var i = 0; i < list.length; i++) pairs.push({ card: list[i], row: rows[i], order: i });
    }

    function cell(p, col) {
        var c = p.row.cells[col];
        if (!c) return '';
        var v = c.getAttribute('data-sort');
        return v !== null ? v : c.textContent.trim();
    }

    // the cards and the table's rows in one order: the option names the column it sorts on (none: the page's own order),
    // the feature's name A to Z, every other column largest first, a tie kept in the page's order
    function sortBy(select) {
        if (!pairs || !pairs.length) return;
        var opt = select.options[select.selectedIndex];
        var col = opt && opt.hasAttribute('data-col') ? parseInt(opt.getAttribute('data-col'), 10) : -1;
        var sorted = pairs.slice().sort(function (a, b) {
            var d = 0;
            if (col === 0) d = cell(a, 0).localeCompare(cell(b, 0), undefined, { numeric: true, sensitivity: 'base' });
            else if (col > 0) d = (parseFloat(cell(b, col)) || 0) - (parseFloat(cell(a, col)) || 0);
            return d || a.order - b.order;
        });
        var body = pairs[0].row.parentNode, table = body.closest('table');
        sorted.forEach(function (p) { body.appendChild(p.row); });
        placeCards(sorted);
        // the table's own column sort no longer holds
        if (table) { table.removeAttribute('data-sort-col'); table.removeAttribute('data-sort-dir'); }
    }

    // The cards show in their new order at once, placed by CSS order in their flex column (5.2.1). A card moved in the
    // page has the layout of every diagram drawn in it thrown away and built again: on 200 scenarios with all 400
    // diagrams drawn, the first sort held the page 213 ms and the frame after it came at 295 ms (later sorts 41 to 53 ms).
    // The page's own order follows after the sort, a slice of a few milliseconds at a time, for what goes by it: Tab,
    // screen readers, the arrow keys and Next Failure. The CSS order holds the picture still meanwhile, and a card moved
    // keeps the height the browser remembered for it, so nothing on the screen shifts. moveBefore would keep a moved
    // card's focus, but its cost lands in the next frame, out of the slice's sight (277 ms there), so a card that held
    // the focus is given it back instead. Anything written around the cards stays put: each goes before the first card
    // or after the one before it.
    var PLACE_SLICE_MS = 8, placing = null;
    function placeCards(sorted) {
        if (placing) clearTimeout(placing);
        sorted.forEach(function (p, i) { p.card.style.order = String(i); });
        var parent = sorted[0].card.parentNode, i = 0;
        function step() {
            placing = null;
            var start = performance.now();
            for (; i < sorted.length; i++) {
                var card = sorted[i].card;
                var first = i === 0 ? parent.querySelector(':scope > details.feature') : null;
                if (i === 0 ? first === card : sorted[i - 1].card.nextElementSibling === card) continue;
                if (performance.now() - start > PLACE_SLICE_MS) { placing = setTimeout(step, 0); return; }
                var focus = document.activeElement, held = focus && card.contains(focus) ? focus : null;
                parent.insertBefore(card, i === 0 ? first : sorted[i - 1].card.nextSibling);
                if (held && document.activeElement !== held) held.focus({ preventScroll: true });
            }
        }
        placing = setTimeout(step, 0);
    }

    // a row of the table opens its feature in the Full view
    function openRow(row) {
        if (!pairs) return;
        for (var i = 0; i < pairs.length; i++) {
            if (pairs[i].row !== row) continue;
            var card = pairs[i].card;
            setView('full');
            card.open = true;
            card.scrollIntoView({ block: 'start' });
            var summary = card.querySelector(':scope > summary');
            if (summary) summary.focus({ preventScroll: true });
            return;
        }
    }
    window.kronikol_open_row = openRow;

    // A fold button with nothing to do is dimmed: expand while every one is open, collapse while none is. Checked once a
    // frame, however many details a click opens.
    function syncFolds() {
        var counts = {};
        Object.keys(FOLDS).forEach(function (noun) {
            var all = document.querySelectorAll(FOLDS[noun]), open = 0;
            for (var i = 0; i < all.length; i++) if (all[i].open) open++;
            counts[noun] = { all: all.length, open: open };
        });
        document.querySelectorAll('.fold-btn[data-fold]').forEach(function (b) {
            var c = counts[b.getAttribute('data-fold')];
            if (!c) return;
            var idle = b.classList.contains('fold-expand') ? c.open === c.all : c.open === 0;
            b.setAttribute('aria-disabled', idle ? 'true' : 'false');
        });
    }
    var queued = false;
    function queueSync() {
        if (queued) return;
        queued = true;
        requestAnimationFrame(function () { queued = false; syncFolds(); });
    }

    // expanding the scenarios opens what holds them too (their rule and feature), or an opened scenario would show nothing
    function fold(noun, opening) {
        var all = document.querySelectorAll(FOLDS[noun]);
        for (var i = 0; i < all.length; i++) {
            if (!opening) { all[i].removeAttribute('open'); continue; }
            all[i].setAttribute('open', '');
            if (noun !== 'scenarios') continue;
            for (var p = all[i].parentElement; p && p.id !== 'report-content'; p = p.parentElement)
                if (p.tagName === 'DETAILS' && !p.open) p.setAttribute('open', '');
        }
        queueSync();
    }

    document.addEventListener('click', function (e) {
        var t = e.target;
        if (!t || !t.closest) return;
        var b = t.closest('.fold-btn[data-fold]');
        if (b) {
            if (b.getAttribute('aria-disabled') !== 'true') fold(b.getAttribute('data-fold'), b.classList.contains('fold-expand'));
            return;
        }
        var tab = t.closest('.view-switch > .view-btn');
        if (tab) { go(tab.getAttribute('data-view')); return; }
        var gear = t.closest('.tools-gear');
        if (gear) {
            var bar = gear.closest('.toolbar-row');
            var open = !!bar && bar.classList.toggle('tools-open');
            gear.setAttribute('aria-expanded', open ? 'true' : 'false');
            return;
        }
        // a row opening its card is a place: Back shows the table where it was (5.2.0)
        var row = t.closest('#features-table-view tbody tr');
        if (row) {
            if (window.kronikol_nav) window.kronikol_nav.go({ row: row });
            else openRow(row);
            return;
        }
    }, true);
    document.addEventListener('keydown', function (e) {
        var t = e.target;
        var tab = t && t.closest ? t.closest('.view-switch > .view-btn') : null;
        if (tab) {
            var all = Array.prototype.slice.call(tab.parentNode.querySelectorAll('.view-btn')), i = all.indexOf(tab), j = -1;
            if (e.key === 'ArrowRight') j = (i + 1) % all.length;
            else if (e.key === 'ArrowLeft') j = (i - 1 + all.length) % all.length;
            else if (e.key === 'Home') j = 0;
            else if (e.key === 'End') j = all.length - 1;
            if (j < 0) return;
            e.preventDefault();
            go(all[j].getAttribute('data-view'), 'view-keys');
            all[j].focus();
            return;
        }
        // the arrow keys move between scenarios, which are in the cards (not while a tab list has the focus, S11)
        if ((e.key === 'ArrowDown' || e.key === 'ArrowUp') && !(t && t.closest && t.closest('[role="tablist"]'))
            && !/^(input|select|textarea)$/i.test((document.activeElement || {}).tagName || '')) {
            // on another tab, the tabs script takes the reader to the Features tab and this view in one step
            var away = document.querySelector('.report-tabs > .report-tab[aria-selected="true"]:not([data-tab="features"])');
            var shown = viewSwitch() && viewSwitch().querySelector('.view-btn[aria-selected="true"]');
            if (!away && shown && shown.getAttribute('data-view') !== 'full') go('full');
        }
    }, true);
    document.addEventListener('change', function (e) {
        if (e.target && e.target.id === 'features-sort') sortBy(e.target);
    });
    document.addEventListener('toggle', queueSync, true);

    // the view an address names is put on by the navigation script, with the tab, as the page opens and on Back
    function start() {
        pair();
        syncFolds();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
    else start();
})();
