<script>
document.addEventListener('click', function(e) {
    var btn = e.target.closest ? e.target.closest('.iflow-toggle-btn') : null;
    if (!btn) return;
    var toggle = btn.closest('.iflow-toggle');
    if (!toggle) return;
    var container = toggle.parentElement;
    if (!container) return;
    var view = btn.getAttribute('data-view');
    toggle.querySelectorAll('.iflow-toggle-btn').forEach(function(b) {
        b.classList.toggle('iflow-toggle-active', b === btn);
    });
    container.querySelectorAll('.iflow-view').forEach(function(v) {
        v.style.display = 'none';
    });
    var target = container.querySelector('.iflow-view-' + view);
    if (target) {
        target.style.display = '';
        window._renderFlameCharts(target);
    }
});
document.addEventListener('click', function(e) {
    var btn = e.target.closest ? e.target.closest('.diagram-toggle-btn') : null;
    if (!btn) return;
    var toggle = btn.closest('.diagram-toggle');
    if (!toggle) return;
    var container = toggle.parentElement;
    if (!container) return;
    var dtype = btn.getAttribute('data-dtype');
    toggle.querySelectorAll('.diagram-toggle-btn').forEach(function(b) {
        var on = b === btn;
        b.classList.toggle('diagram-toggle-active', on);
        b.setAttribute('aria-selected', on ? 'true' : 'false');
        b.tabIndex = on ? 0 : -1;
    });
    // the scenario's settings act on its sequence diagrams, so they step aside while another view shows
    if (container.querySelector(':scope > .scenario-settings')) container.classList.toggle('diagrams-not-seq', dtype !== 'seq');
    container.querySelectorAll('.diagram-view').forEach(function(v) {
        v.style.display = 'none';
    });
    var target = container.querySelector('.diagram-view-' + dtype);
    if (target) {
        target.style.display = '';
        if (window._renderFlameCharts) window._renderFlameCharts(target);
        // The view's diagrams are drawn now it is shown, rather than whenever the page's observer notices it.
        if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(target);
    }
});
// The views are tabs: the arrow keys, Home and End move between them, showing each as they go.
document.addEventListener('keydown', function(e) {
    var btn = e.target.closest ? e.target.closest('.diagram-toggle-tabs > .diagram-toggle-btn') : null;
    if (!btn) return;
    var all = Array.prototype.slice.call(btn.parentElement.querySelectorAll(':scope > .diagram-toggle-btn'));
    var i = all.indexOf(btn), j = -1;
    if (e.key === 'ArrowRight') j = (i + 1) % all.length;
    else if (e.key === 'ArrowLeft') j = (i - 1 + all.length) % all.length;
    else if (e.key === 'Home') j = 0;
    else if (e.key === 'End') j = all.length - 1;
    if (j < 0) return;
    e.preventDefault();
    all[j].click();
    all[j].focus();
});
</script>