// The run band's layout (5.0.0, plans/OPTION_C_PLAN.md S2, D4): the ring, the summary and the CI zone share one row
// while all three fit whole; otherwise the CI zone takes a second row under the summary (.ci-below). Decided on the
// zones' measured natural widths, never by watching the row wrap, so the switch cannot oscillate.
(function () {
    var needs = null;
    function natural(band, zone) {
        var copy = zone.cloneNode(true);
        copy.style.cssText += ';position:absolute;visibility:hidden;left:-99999px;top:0;width:max-content;padding-left:0;border-left:none;';
        band.appendChild(copy);
        var width = copy.getBoundingClientRect().width;
        copy.remove();
        return width;
    }
    function layout() {
        var band = document.querySelector('.run-band');
        if (!band) return;
        var summary = band.querySelector('.run-summary'), ci = band.querySelector('.run-ci');
        if (!summary || !ci) return;
        var style = getComputedStyle(band);
        if (needs == null) {
            var ring = band.querySelector('.run-ring');
            var gap = parseFloat(style.columnGap) || 0;
            // the CI zone's divider: a 1px rule and 24px of padding while it shares the row
            needs = (ring ? ring.getBoundingClientRect().width + gap : 0) + natural(band, summary) + gap + 25 + natural(band, ci);
        }
        var available = band.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
        band.classList.toggle('ci-below', needs > available);
    }
    function remeasure() { needs = null; layout(); }
    // measured again at most once a frame, and never inside a ResizeObserver callback, whose class change would
    // resize what it observes in the same frame
    var queued = false;
    function queue() {
        if (queued) return;
        queued = true;
        requestAnimationFrame(function () { queued = false; remeasure(); });
    }
    // a phone's narrower ring and gaps change what the row needs, and so does the zones' text: a reader's text
    // spacing or a font landing after load. The natural widths do not depend on the band's layout, so moving the CI
    // zone measures the same widths again and the decision holds.
    function start() {
        layout();
        var band = document.querySelector('.run-band');
        if (!band || !window.ResizeObserver) return;
        var observer = new ResizeObserver(queue);
        Array.prototype.forEach.call(band.querySelectorAll('.run-summary, .run-ci'), function (zone) { observer.observe(zone); });
    }
    window.addEventListener('resize', queue);
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
    else start();
    if (document.fonts && document.fonts.ready) document.fonts.ready.then(remeasure);
})();
