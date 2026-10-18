// Light or dark before anything is painted (5.0.0, plans/OPTION_C_PLAN.md S1): the reader's pick, kept by the switch on
// the title line (report-theme-switch-script.js), else the system's setting. The theme is the one thing a report keeps
// in the browser; filters and toggles live in the page's link.
(function () {
    var t = null;
    try { t = localStorage.getItem('kronikol.theme'); } catch (e) { /* storage blocked: the system's setting */ }
    if (t !== 'dark' && t !== 'light') t = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    document.documentElement.setAttribute('data-theme', t);
})();
