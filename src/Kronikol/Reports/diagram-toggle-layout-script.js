<script>
// Scenario diagram tabs layout. The Sequence / Activity / Flame tabs of a scenario with several kinds of
// diagram float to the right of the "Diagrams" title, on the title line, when both fit side by side;
// when the section is too narrow for that (a phone, a narrow pane) they go back under the title, full
// width. Measured, not guessed: the widths depend on the title, the font and the tabs a scenario has,
// so no fixed breakpoint is right for every report. The CSS lives under
// `.diagram-toggle[data-layout="inline"]`. Since 5.0.0 the tabs have a bar of their own and a
// scenario's settings sit after it, never on the title line.
(function () {
    var GAP = 16; // px kept between the end of the title text and the toolbar
    var scheduled = false;

    var NEAR = 0.5; // of the screen's height: how far from it a bar is laid out, ahead of being seen
    // The sections the ResizeObserver saw take a size since the last pass. The page laid each out to measure it, so its
    // bar is laid out too however far from the screen it is: the page lays out what is within a screen and a half of the
    // screen (content-visibility: auto), farther than NEAR, and a bar whose section took its size out there had nothing
    // to lay it out again when it came near (5.2.5). The observer never reports a section the page has not laid out.
    var grown = new Set();

    // A bar in a closed <details> or a hidden panel is not drawn, and the page's structure says so without a layout; it
    // keeps the layout it had, and is laid out again when it shows (its <details> opening, or its panel, sets one off).
    function drawn(toggle) {
        var summary = toggle.previousElementSibling;
        return !!summary && summary.tagName === 'SUMMARY' && !toggle.closest('details:not([open]), [hidden]');
    }

    // The bars to lay out: those drawn and within half a screen of it. The page lays out only the features and scenarios
    // near the screen (content-visibility: auto), and a box read in one it skipped lays that one out for the reading, so
    // with every scenario of 750 open a click held the page for seconds (5.1.0): a feature's box is read before anything
    // in it, then its scenario's, and nothing is read in one far from the screen. A bar farther off keeps the layout it
    // had, and is laid out as its section takes a size: the page lays its scenario out as it comes near, and the
    // ResizeObserver in start sees the section take its size.
    function nearToggles() {
        var margin = window.innerHeight * NEAR, near = new Map();
        function isNear(section) {
            if (!section) return true;
            var v = near.get(section);
            if (v === undefined) {
                var r = section.getBoundingClientRect();
                v = r.bottom >= -margin && r.top <= window.innerHeight + margin;
                near.set(section, v);
            }
            return v;
        }
        var all = document.querySelectorAll('summary + .diagram-toggle'), toggles = [];
        for (var i = 0; i < all.length; i++) {
            var toggle = all[i];
            if (!drawn(toggle)) continue;
            if (grown.has(toggle.parentElement) || (isNear(toggle.closest('.feature')) && isNear(toggle.closest('.scenario')))) toggles.push(toggle);
        }
        grown.clear();
        return toggles;
    }

    // Every bar near the screen is laid out at once: each tries the inline layout (float right, shrink-wrapped), all are
    // read in one layout, and each keeps it where it clears its title. One bar at a time laid the page out once a bar,
    // and a Features tab of 750 scenarios with internal flow held the page for seconds, on every click and on opening the
    // tab (5.1.0). A bar's fit and centring depend on its own section alone, so trying them all together reads the same.
    function layoutAll() {
        scheduled = false;
        var toggles = nearToggles();
        toggles.forEach(function (toggle) {
            toggle.setAttribute('data-layout', 'inline');
            toggle.style.marginTop = '';
        });
        var range = document.createRange();
        var reads = toggles.map(function (toggle) {
            var summary = toggle.previousElementSibling;
            range.selectNodeContents(summary);
            return { toggle: toggle.getBoundingClientRect(), text: range.getBoundingClientRect(), summary: summary.getBoundingClientRect() };
        });
        toggles.forEach(function (toggle, k) {
            var r = reads[k];
            // not rendered after all (a scenario the filters hide): nothing to lay out
            var fits = r.toggle.width > 0 && (r.toggle.left - r.text.right) >= GAP && r.toggle.height <= r.summary.height;
            if (!fits) {
                toggle.removeAttribute('data-layout');
                return;
            }
            // It sits after the summary in flow: pull it up so it is centred on the title line.
            toggle.style.marginTop = (-(r.summary.height + r.toggle.height) / 2) + 'px';
        });
    }

    function schedule() {
        if (scheduled) return;
        scheduled = true;
        requestAnimationFrame(layoutAll);
    }
    window._layoutDiagramToggles = schedule;

    function start() {
        layoutAll();
        if (document.fonts && document.fonts.ready) document.fonts.ready.then(schedule);
        window.addEventListener('resize', schedule);
        // Opening a scenario or its diagrams section renders a bar that had no size before.
        document.addEventListener('click', schedule, true);
        document.addEventListener('toggle', schedule, true);
        // a bar's section taking a size (laid out as it comes near, scrolled to or brought there by a sort) lays it out
        if (typeof ResizeObserver === 'function') {
            var ro = new ResizeObserver(function (entries) {
                entries.forEach(function (e) { grown.add(e.target); });
                schedule();
            });
            var hosts = document.querySelectorAll('summary + .diagram-toggle');
            for (var i = 0; i < hosts.length; i++) if (hosts[i].parentElement) ro.observe(hosts[i].parentElement);
        }
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
    else start();
})();
</script>
