<script>
(function() {
    var MIN_BAR_PCT = 0; // minimum display width enforced via CSS min-width

    function buildBar(source, name, leftPct, widthPct, depth, durMs, totalDurMs) {
        var hue = Math.abs(hashCode(source)) % 360;
        var lightness = 70 + Math.min(depth * 5, 20);
        var durText = durMs >= 1 ? ' (' + durMs + 'ms)' : '';
        var pctText = totalDurMs > 0 ? ' — ' + (durMs / totalDurMs * 100).toFixed(1) + '% of total' : '';
        return '<div class="iflow-flame-bar" style="margin-left:' + leftPct.toFixed(2)
            + '%;width:' + widthPct.toFixed(2) + '%;background:hsl(' + hue + ', 60%, ' + lightness
            + '%)" data-left="' + leftPct.toFixed(4) + '" data-width="' + widthPct.toFixed(4)
            + '" title="[' + escHtml(source) + '] ' + escHtml(name) + durText + pctText
            + '"><span class="iflow-flame-label">' + escHtml(name) + durText + '</span></div>';
    }

    function renderFlameData(container, data, viewLeft, viewRight) {
        if (!data || !data.s || !data.f || data.f.length === 0) return;
        if (!viewLeft && !viewRight && container.dataset.flameRendered) return;
        container.dataset.flameRendered = '1';

        var sources = data.s;
        var hasMarkers = data.m && data.m.length > 0;
        if (hasMarkers) container.style.position = 'relative';
        var isZoomed = viewLeft != null && viewRight != null;
        var vl = viewLeft || 0, vr = viewRight || 100;
        var vw = vr - vl;

        // Compute total duration from max span end minus min span start
        var totalDurMs = 0;
        if (data.f.length > 0) {
            var minLeft = 100, maxRight = 0;
            for (var i = 0; i < data.f.length; i++) {
                var sp = data.f[i];
                if (sp[2] < minLeft) minLeft = sp[2];
                var right = sp[2] + sp[3];
                if (right > maxRight) maxRight = right;
            }
            // Sum leaf durations for total (approximate)
            for (var i = 0; i < data.f.length; i++) totalDurMs += data.f[i][5] || 0;
        }

        var html = [];
        if (isZoomed) {
            html.push('<div class="iflow-flame-zoom-hint" title="Double-click to reset zoom">🔍 Zoomed — double-click to reset</div>');
        }
        if (hasMarkers) {
            for (var mi = 0; mi < data.m.length; mi++) {
                var m = data.m[mi];
                var mPos = isZoomed ? (m[0] - vl) / vw * 100 : m[0];
                if (isZoomed && (m[0] < vl || m[0] > vr)) continue;
                html.push('<div class="iflow-boundary-marker" style="left:' + mPos.toFixed(2) + '%" title="' + escHtml(m[1]) + '"></div>');
            }
        }
        for (var i = 0; i < data.f.length; i++) {
            var sp = data.f[i];
            var srcIdx = sp[0], name = sp[1], leftPct = sp[2], widthPct = sp[3], depth = sp[4], durMs = sp[5];
            if (isZoomed) {
                var right = leftPct + widthPct;
                if (right < vl || leftPct > vr) continue; // outside viewport
                var clampedLeft = Math.max(leftPct, vl);
                var clampedRight = Math.min(right, vr);
                leftPct = (clampedLeft - vl) / vw * 100;
                widthPct = (clampedRight - clampedLeft) / vw * 100;
            }
            var source = sources[srcIdx];
            html.push(buildBar(source, name, leftPct, widthPct, depth, durMs, totalDurMs));
        }
        container.innerHTML = html.join('');
    }

    function renderSequentialFlameData(container, data) {
        if (!data || !data.s || !data.b || data.b.length === 0) return;
        if (container.dataset.flameRendered) return;
        container.dataset.flameRendered = '1';
        var sources = data.s;
        var html = [];
        for (var bi = 0; bi < data.b.length; bi++) {
            var band = data.b[bi];
            var totalDurMs = 0;
            for (var i = 0; i < band.f.length; i++) totalDurMs += band.f[i][5] || 0;
            html.push('<div class="iflow-test-band"><div class="iflow-test-band-label">' + escHtml(band.id) + '</div>');
            for (var i = 0; i < band.f.length; i++) {
                var sp = band.f[i];
                var srcIdx = sp[0], name = sp[1], leftPct = sp[2], widthPct = sp[3], depth = sp[4], durMs = sp[5];
                var source = sources[srcIdx];
                html.push(buildBar(source, name, leftPct, widthPct, depth, durMs, totalDurMs));
            }
            html.push('</div>');
        }
        container.innerHTML = html.join('');
    }

    function escHtml(s) {
        return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
    }

    function hashCode(s) {
        var h = 0;
        for (var i = 0; i < s.length; i++) {
            h = ((h << 5) - h + s.charCodeAt(i)) | 0;
        }
        return h;
    }

    // Decompress gzip+base64 data (used for whole-test-flow compressed attributes)
    function decompressBase64(base64) {
        var raw = atob(base64);
        var bytes = new Uint8Array(raw.length);
        for (var i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
        var stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream('gzip'));
        return new Response(stream).text();
    }

    // Click-to-zoom: click a bar to zoom into its time range, double-click to reset
    function attachZoomHandlers(container, data) {
        container.addEventListener('click', function(e) {
            var bar = e.target.closest('.iflow-flame-bar');
            if (!bar) return;
            var left = parseFloat(bar.getAttribute('data-left'));
            var width = parseFloat(bar.getAttribute('data-width'));
            if (isNaN(left) || isNaN(width) || width <= 0) return;
            // Zoom to this bar with 5% padding on each side
            var pad = width * 0.05;
            var vl = Math.max(0, left - pad);
            var vr = Math.min(100, left + width + pad);
            container.dataset.flameRendered = '';
            renderFlameData(container, data, vl, vr);
            container._flameData = data;
        });
        container.addEventListener('dblclick', function(e) {
            e.preventDefault();
            var d = container._flameData || data;
            container.dataset.flameRendered = '';
            renderFlameData(container, d);
            container._flameData = d;
        });
    }

    // Render all data-flame elements within a container (or document)
    function renderFlameCharts(root) {
        var els = (root || document).querySelectorAll('.iflow-flame[data-flame]');
        for (var i = 0; i < els.length; i++) {
            if (els[i].dataset.flameRendered) continue;
            try {
                var data = JSON.parse(els[i].getAttribute('data-flame'));
                renderFlameData(els[i], data);
                attachZoomHandlers(els[i], data);
            } catch(e) {}
        }
        // Handle compressed flame data (whole-test-flow)
        var zEls = (root || document).querySelectorAll('.iflow-flame[data-flame-z]');
        for (var i = 0; i < zEls.length; i++) {
            if (zEls[i].dataset.flameRendered) continue;
            (function(el) {
                el.dataset.flameRendered = '1';
                decompressBase64(el.getAttribute('data-flame-z')).then(function(json) {
                    el.dataset.flameRendered = '';
                    var data = JSON.parse(json);
                    renderFlameData(el, data);
                    attachZoomHandlers(el, data);
                }).catch(function() {});
            })(zEls[i]);
        }
        var seqEls = (root || document).querySelectorAll('.iflow-sequential-tests[data-flame]');
        for (var i = 0; i < seqEls.length; i++) {
            if (seqEls[i].dataset.flameRendered) continue;
            try {
                var data = JSON.parse(seqEls[i].getAttribute('data-flame'));
                renderSequentialFlameData(seqEls[i], data);
            } catch(e) {}
        }
    }

    // Render flame charts from flameData property in popup segments
    function renderPopupFlameCharts(container, flameData) {
        if (!flameData) return;
        var el = container.querySelector('.iflow-flame[data-diagram-type="flamechart"]');
        if (el) {
            renderFlameData(el, flameData);
            attachZoomHandlers(el, flameData);
        }
    }

    // Expose globally
    window._renderFlameCharts = renderFlameCharts;
    window._renderPopupFlameCharts = renderPopupFlameCharts;

    // Auto-render visible data-flame elements on page load
    document.addEventListener('DOMContentLoaded', function() {
        renderFlameCharts(document);

        // Render on details expand
        document.addEventListener('toggle', function(e) {
            if (e.target.tagName === 'DETAILS' && e.target.open) {
                renderFlameCharts(e.target);
            }
        }, true);
    });
})();
</script>