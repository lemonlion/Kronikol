<script>
// Scenario diagram toolbar layout. The Details / Headers / Assertions / Steps / Databases buttons
// (and the Sequence / Activity / Flame tabs when a scenario has several diagram types) float to the
// right of the "Sequence Diagrams" / "Diagrams" title, on the title line, when both fit side by
// side; when the container is too narrow for that — a phone, a narrow pane, a scenario with many
// toggles — they go back under the title, full width, as before. Measured, not guessed: the widths
// depend on the title, the font and on which toggles a scenario has, so no fixed breakpoint is right
// for every report. The CSS lives under `.diagram-toggle[data-layout="inline"]`.
(function () {
    var GAP = 16; // px kept between the end of the title text and the toolbar
    var scheduled = false;

    function layoutOne(toggle) {
        var summary = toggle.previousElementSibling;
        if (!summary || summary.tagName !== 'SUMMARY') return;
        // Try the inline layout (float right, shrink-wrapped), then check that it clears the title.
        toggle.setAttribute('data-layout', 'inline');
        toggle.style.marginTop = '';
        var toggleRect = toggle.getBoundingClientRect();
        if (!(toggleRect.width > 0)) { // not rendered (its <details> is closed) — nothing to lay out
            toggle.removeAttribute('data-layout');
            return;
        }
        var range = document.createRange();
        range.selectNodeContents(summary);
        var textRect = range.getBoundingClientRect();
        var summaryRect = summary.getBoundingClientRect();
        var fits = (toggleRect.left - textRect.right) >= GAP && toggleRect.height <= summaryRect.height;
        if (!fits) {
            toggle.removeAttribute('data-layout');
            toggle.style.marginTop = '';
            return;
        }
        // It sits after the summary in flow: pull it up so it is centred on the title line.
        toggle.style.marginTop = (-(summaryRect.height + toggleRect.height) / 2) + 'px';
    }

    function layoutAll() {
        scheduled = false;
        var toggles = document.querySelectorAll('summary + .diagram-toggle');
        for (var i = 0; i < toggles.length; i++) layoutOne(toggles[i]);
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
        // Button labels change width ("Headers Shown" → "Headers Hidden"), report-level toggles
        // rewrite every scenario's buttons, and opening a scenario or its diagrams section renders a
        // toolbar that had no size before.
        document.addEventListener('click', schedule, true);
        document.addEventListener('toggle', schedule, true);
        if (typeof ResizeObserver === 'function') {
            var ro = new ResizeObserver(schedule);
            var hosts = document.querySelectorAll('summary + .diagram-toggle');
            for (var i = 0; i < hosts.length; i++) if (hosts[i].parentElement) ro.observe(hosts[i].parentElement);
        }
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
    else start();
})();
</script>
