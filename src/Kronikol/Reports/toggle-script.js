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
        b.classList.toggle('diagram-toggle-active', b === btn);
    });
    container.querySelectorAll('.diagram-view').forEach(function(v) {
        v.style.display = 'none';
    });
    var target = container.querySelector('.diagram-view-' + dtype);
    if (target) {
        target.style.display = '';
        if (window._renderFlameCharts) window._renderFlameCharts(target);
    }
});
</script>