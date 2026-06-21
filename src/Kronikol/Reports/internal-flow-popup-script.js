<script>
(function() {
    var iflowData = window.__iflowSegments || {};

    function showPopup(segmentId) {
        var existing = document.querySelector('.iflow-overlay');
        if (existing) existing.remove();

        var overlay = document.createElement('div');
        overlay.className = 'iflow-overlay';

        var popup = document.createElement('div');
        popup.className = 'iflow-popup';

        var closeBtn = document.createElement('button');
        closeBtn.className = 'iflow-popup-close';
        closeBtn.innerHTML = '&times;';
        closeBtn.onclick = function() { overlay.remove(); };
        popup.appendChild(closeBtn);

        var segment = iflowData[segmentId];
        if (segment && segment.content) {
            var header = document.createElement('h3');
            header.textContent = segment.title || 'Internal Flow';
            popup.appendChild(header);

            var diagramDiv = document.createElement('div');
            diagramDiv.className = 'iflow-diagram';
            diagramDiv.innerHTML = segment.content;
            popup.appendChild(diagramDiv);

            // Render flame chart from data if available
            if (segment.flameData && window._renderPopupFlameCharts) {
                window._renderPopupFlameCharts(diagramDiv, segment.flameData);
            }

            if (window.plantuml && diagramDiv.querySelector('.plantuml-browser')) {
                diagramDiv.querySelectorAll('.plantuml-browser').forEach(function(el) {
                    el.dataset.queued = '1';
                    function renderEl(source) {
                        try {
                            var lines = source.split('\n');
                            if (lines.length > 3000) {
                                el.dataset.rendered = '1';
                                el.innerHTML = '<div style="color:#c00;padding:1em;border:1px solid #c00;border-radius:6px">' +
                                    '<strong>Activity diagram too large for browser rendering (' + lines.length + ' lines).</strong><br>' +
                                    'Use <code>CallTree</code> style for large relationship flows.</div>';
                                return;
                            }
                            var mo = new MutationObserver(function() {
                                mo.disconnect();
                                el.dataset.rendered = '1';
                            });
                            mo.observe(el, { childList: true, subtree: true });
                            window.plantuml.render(lines, el.id);
                            setTimeout(function() {
                                var text = el.textContent || '';
                                if (text.indexOf('RuntimeException') >= 0 || text.indexOf('RangeError') >= 0) {
                                    mo.disconnect();
                                    el.dataset.rendered = '1';
                                    el.innerHTML = '<div style="color:#c00;padding:1em;border:1px solid #c00;border-radius:6px">' +
                                        '<strong>Activity diagram too large for browser rendering.</strong><br>' +
                                        'Use <code>CallTree</code> style for large relationship flows.</div>';
                                }
                            }, 100);
                        } catch(e) {
                            el.dataset.rendered = '1';
                            el.textContent = 'Activity diagram too large for browser rendering. Use CallTree style instead.';
                            el.style.color = '#c00';
                        }
                    }
                    var source = el.getAttribute('data-plantuml');
                    if (source) {
                        renderEl(source);
                    } else {
                        var pumlZ = window._getPumlZ ? window._getPumlZ(el) : el.getAttribute('data-plantuml-z');
                        if (pumlZ) {
                            decompressGzipBase64(pumlZ).then(function(decoded) {
                                el.setAttribute('data-plantuml', decoded);
                                renderEl(decoded);
                            }).catch(function() { el.dataset.rendered = '1'; el.textContent = 'Decompression error'; });
                        }
                    }
                });
            }
        } else {
            var noData = document.createElement('div');
            noData.className = 'iflow-no-data';
            noData.textContent = segment && segment.message
                ? segment.message
                : 'No internal flow data available for this segment.';
            popup.appendChild(noData);
        }

        overlay.appendChild(popup);

        // Wire up toggle buttons if present
        var toggleBtns = popup.querySelectorAll('.iflow-toggle-btn');
        if (toggleBtns.length) {
            toggleBtns.forEach(function(btn) {
                btn.addEventListener('click', function() {
                    var view = btn.getAttribute('data-view');
                    var container = popup.querySelector('.iflow-diagram');
                    if (!container) return;
                    toggleBtns.forEach(function(b) { b.classList.remove('iflow-toggle-active'); });
                    btn.classList.add('iflow-toggle-active');
                    var main = container.querySelector('.iflow-view-main');
                    var flame = container.querySelector('.iflow-view-flame');
                    if (main) main.style.display = view === 'main' ? '' : 'none';
                    if (flame) flame.style.display = view === 'flame' ? '' : 'none';
                });
            });
        }

        overlay.addEventListener('click', function(e) {
            if (e.target === overlay) overlay.remove();
        });
        document.body.appendChild(overlay);
    }

    // Expose for direct binding from the render script
    window._iflowShowPopup = showPopup;

    // Fallback: document-level click handler (capture phase for IKVM/server SVG compatibility)
    document.addEventListener('click', function(e) {
        var el = e.target;
        while (el && el !== document) {
            if (el.localName === 'a') {
                var href = el.getAttribute('xlink:href') || el.getAttribute('href') || '';
                if (href.indexOf('#iflow-') === 0) {
                    e.preventDefault();
                    e.stopPropagation();
                    showPopup(href.substring(1));
                    return;
                }
            }
            el = el.parentNode;
        }
    }, true);

    document.addEventListener('keydown', function(e) {
        if (e.key === 'Escape') {
            var overlay = document.querySelector('.iflow-overlay');
            if (overlay) overlay.remove();
        }
    });
})();
</script>