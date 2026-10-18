// The report's tabs (5.0.0, plans/OPTION_C_PLAN.md S3, D20): the bar's tabs show one panel at a time. The generator
// writes the tabs, the panels and which one the page opens on; this script switches them, moves between them with the
// arrow keys, takes anything that goes to a scenario to the Features tab first, and tells the page how tall the bar is
// while it sticks to the top, so a scenario scrolled to is not left under it.
(function () {
    function bar() { return document.querySelector('.toolbar-row.report-nav'); }
    function tabs() { return Array.prototype.slice.call(document.querySelectorAll('.report-tabs > .report-tab')); }
    function panel(key) { return document.getElementById('panel-' + key); }

    function activate(key) {
        var target = panel(key);
        if (!target || !target.hasAttribute('role')) return;
        tabs().forEach(function (tab) {
            var k = tab.getAttribute('data-tab'), on = k === key, p = panel(k);
            if (p) p.hidden = !on;
            tab.setAttribute('aria-selected', on ? 'true' : 'false');
            tab.tabIndex = on ? 0 : -1;
        });
        var nav = bar();
        if (nav) nav.classList.toggle('on-features', key === 'features');
        // a diagram in a panel that was hidden is drawn when the panel shows (the engine is not fetched for a hidden tab)
        if (typeof window._renderDiagramsInContainer === 'function') window._renderDiagramsInContainer(target);
        window.dispatchEvent(new Event('resize'));
    }
    window.kronikol_activate_tab = activate;

    function selected() {
        var on = tabs().filter(function (t) { return t.getAttribute('aria-selected') === 'true'; })[0];
        return on ? on.getAttribute('data-tab') : null;
    }
    function toFeatures() { if (selected() && selected() !== 'features') activate('features'); }

    // the address names a scenario (`#scenario-<slug>` or `#sid-<id>`, ahead of any filter state)
    function hashNamesScenario() {
        var first = window.location.hash.substring(1).split('&')[0];
        return first.indexOf('scenario-') === 0 || first.indexOf('sid-') === 0;
    }

    // registered as the script runs, in the head, so each runs before the report's own handler for the same event
    window.addEventListener('hashchange', function () { if (hashNamesScenario()) toFeatures(); });
    document.addEventListener('click', function (e) {
        var t = e.target;
        if (!t || !t.closest) return;
        var tab = t.closest('.report-tabs > .report-tab');
        if (tab) { activate(tab.getAttribute('data-tab')); return; }
        // a link to a scenario names it by its address (a failure cluster's, a history entry's, a diagnostic's), and
        // Next Failure goes to one: named by kind, never by section, so a page without a section names none of it
        if (t.closest('.jump-to-failure, a[href^="#scenario-"], a[href^="#sid-"]')) toFeatures();
        // the summary of a section in a tab of its own is its heading: the tab is its disclosure, so it stays open
        if (t.closest('.report-panel:not(#panel-features) > details > summary')) e.preventDefault();
    }, true);
    document.addEventListener('keydown', function (e) {
        var tab = e.target && e.target.closest ? e.target.closest('.report-tabs > .report-tab') : null;
        if (tab) {
            var all = tabs(), i = all.indexOf(tab), j = -1;
            if (e.key === 'ArrowRight') j = (i + 1) % all.length;
            else if (e.key === 'ArrowLeft') j = (i - 1 + all.length) % all.length;
            else if (e.key === 'Home') j = 0;
            else if (e.key === 'End') j = all.length - 1;
            if (j < 0) return;
            e.preventDefault();
            activate(all[j].getAttribute('data-tab'));
            all[j].focus();
            return;
        }
        // the arrow keys move between scenarios, which are on the Features tab
        if ((e.key === 'ArrowDown' || e.key === 'ArrowUp') && !/^(input|select|textarea)$/i.test((document.activeElement || {}).tagName || '')) toFeatures();
    }, true);

    // the bar's height while it sticks, for the scroll margins of what scrolls under it; none while it does not
    function measure() {
        var nav = bar();
        var h = nav && getComputedStyle(nav).position === 'sticky' ? nav.getBoundingClientRect().height : 0;
        document.documentElement.style.setProperty('--kron-nav-h', Math.round(h) + 'px');
    }
    function start() {
        if (hashNamesScenario()) toFeatures();
        measure();
        var nav = bar();
        if (nav && window.ResizeObserver) new ResizeObserver(function () { requestAnimationFrame(measure); }).observe(nav);
        window.addEventListener('resize', measure);
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
    else start();
})();
