function filter_duration() {
    var c = fc();
    var input = document.getElementById('duration-threshold');
    if (!input) return;
    var threshold = parseFloat(input.value);
    if (isNaN(threshold) || threshold <= 0) {
        for (var i = 0; i < c.items.length; i++) c.items[i].dur = false;
        applyVisibility(c);
        update_url_hash();
        return;
    }
    // Whole milliseconds, as the attributes below are written, so a threshold of 0.03 s is 30, not 30.000000000000004.
    var thresholdMs = Math.round(threshold * 1000);
    for (var i = 0; i < c.items.length; i++) {
        if (!c.items[i].el.hasAttribute('data-duration-ms')) { c.items[i].dur = true; continue; }
        var ms = parseFloat(c.items[i].el.getAttribute('data-duration-ms'));
        // Each scenario's own time: the first-call warm-up it paid (data-warmup-ms) is left out, as the percentiles are.
        var warmUp = parseFloat(c.items[i].el.getAttribute('data-warmup-ms') || '0');
        c.items[i].dur = ms > 0 && ms - warmUp < thresholdMs;
    }
    applyVisibility(c);
    update_url_hash();
}
function set_percentile(btn) {
    var wasActive = btn.classList.contains('percentile-active');
    document.querySelectorAll('.percentile-btn').forEach(function(b) { b.classList.remove('percentile-active'); });
    var input = document.getElementById('duration-threshold');
    var customWrap = document.getElementById('custom-duration-wrap');
    if (wasActive) {
        if (input) { input.value = ''; filter_duration(); }
        if (customWrap) customWrap.style.display = 'none';
        return;
    }
    var isCustom = btn.getAttribute('data-custom') === '1';
    if (isCustom) {
        btn.classList.add('percentile-active');
        if (customWrap) customWrap.style.display = 'inline-flex';
        if (input) { input.focus(); if (input.value) filter_duration(); }
    } else {
        if (customWrap) customWrap.style.display = 'none';
        btn.classList.add('percentile-active');
        var ms = parseFloat(btn.getAttribute('data-threshold-ms'));
        // The percentile itself, not rounded to a tenth of a second: a P50 of 30 ms read 0.0, which filters nothing, and a
        // P90 of 1,259 ms read 1.3, which hid the scenario it names.
        if (input) { input.value = String(ms / 1000); filter_duration(); }
    }
}