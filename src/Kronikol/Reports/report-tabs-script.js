// The report's tabs (5.0.0, plans/OPTION_C_PLAN.md S3, D20): the bar's tabs show one panel at a time. The generator
// writes the tabs, the panels and which one the page opens on; this script switches them, moves between them with the
// arrow keys, and tells the page how tall the bar is while it sticks to the top, so a scenario scrolled to is not left
// under it. Since 5.2.0 a tab is a place: a click, or a run of arrow keys, goes through the navigation script
// (report-navigation-script.js), which adds the entry Back returns by, and takes anything that goes to a scenario to the
// Features tab (plans/BACK_BUTTON_NAVIGATION_PLAN.md).
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
        // a diagram in a panel that was hidden is drawn when the panel shows (the engine is not fetched for a hidden tab),
        // and only one that shows: the Features tab holds every scenario's diagrams, most of them in closed scenarios
        if (typeof window._renderDiagramsInContainer === 'function') window._renderDiagramsInContainer(target, true);
        window.dispatchEvent(new Event('resize'));
    }
    window.kronikol_activate_tab = activate;

    function selected() {
        var on = tabs().filter(function (t) { return t.getAttribute('aria-selected') === 'true'; })[0];
        return on ? on.getAttribute('data-tab') : null;
    }
    // a tab chosen by the reader is a place (5.2.0); key names a run of arrow keys, which is one entry
    function go(key, run) {
        if (window.kronikol_nav) window.kronikol_nav.go({ tab: key }, run ? { key: run } : null);
        else activate(key);
    }
    // the arrow keys between scenarios take the reader to them: the Features tab, and its Full view, as one place
    function toFeatures() {
        if (!selected() || selected() === 'features') return;
        if (window.kronikol_nav) window.kronikol_nav.go({ tab: 'features', view: 'full' });
        else activate('features');
    }

    // registered as the script runs, in the head, so each runs before the report's own handler for the same event
    document.addEventListener('click', function (e) {
        var t = e.target;
        if (!t || !t.closest) return;
        var tab = t.closest('.report-tabs > .report-tab');
        if (tab) { go(tab.getAttribute('data-tab')); return; }
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
            go(all[j].getAttribute('data-tab'), 'tab-keys');
            all[j].focus();
            return;
        }
        // the arrow keys move between scenarios, which are on the Features tab (not while a tab list has the focus: its
        // own keys are Left, Right, Home and End, and Up and Down there moved a scenario in a hidden panel, S11)
        if ((e.key === 'ArrowDown' || e.key === 'ArrowUp') && !(e.target && e.target.closest && e.target.closest('[role="tablist"]'))
            && !/^(input|select|textarea)$/i.test((document.activeElement || {}).tagName || '')) toFeatures();
    }, true);

    // the bar's height while it sticks, for the scroll margins of what scrolls under it; none while it does not
    function measure() {
        var nav = bar();
        var h = nav && getComputedStyle(nav).position === 'sticky' ? nav.getBoundingClientRect().height : 0;
        document.documentElement.style.setProperty('--kron-nav-h', Math.round(h) + 'px');
    }
    // the navigation script measures before it scrolls to a place as the page opens, which is before start runs
    window.kronikol_measure_bar = measure;
    function start() {
        measure();
        var nav = bar();
        if (nav && window.ResizeObserver) new ResizeObserver(function () { requestAnimationFrame(measure); }).observe(nav);
        window.addEventListener('resize', measure);
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
    else start();
})();
