// The theme switch on the title line (5.0.0, plans/OPTION_C_PLAN.md S1). The boot script in the head chose the theme
// before the first paint; a click flips it and keeps the reader's pick, and until there is one, the page follows the
// system's setting as it changes.
(function () {
    var root = document.documentElement;
    var system = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;
    // the reader's pick on this page, which holds where the browser refuses to store it
    var picked = null;
    function stored() {
        if (picked) return picked;
        try { var t = localStorage.getItem('kronikol.theme'); return t === 'dark' || t === 'light' ? t : null; }
        catch (e) { return null; }
    }
    function current() { return root.getAttribute('data-theme') === 'dark' ? 'dark' : 'light'; }
    function sync() {
        var button = document.querySelector('.theme-toggle');
        if (!button) return;
        var dark = current() === 'dark';
        button.setAttribute('aria-pressed', String(dark));
        button.title = dark ? 'Switch to the light theme' : 'Switch to the dark theme';
    }
    function set(t, keep) {
        root.setAttribute('data-theme', t);
        if (keep) {
            picked = t;
            try { localStorage.setItem('kronikol.theme', t); } catch (e) { /* storage refused: this page only */ }
        }
        sync();
    }
    // a copy of the page whose head lost the boot script
    if (!root.hasAttribute('data-theme')) set(stored() || (system && system.matches ? 'dark' : 'light'), false);
    if (system) {
        var follow = function (e) { if (!stored()) set(e.matches ? 'dark' : 'light', false); };
        if (system.addEventListener) system.addEventListener('change', follow);
        else if (system.addListener) system.addListener(follow);
    }
    document.addEventListener('click', function (e) {
        if (e.target.closest && e.target.closest('.theme-toggle')) set(current() === 'dark' ? 'light' : 'dark', true);
    });
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', sync);
    else sync();
})();
