<script>
(function() {
    // The segment map is one element, <script id="iflow-segments" type="application/json">, holding
    // {"has" or "hidden": [ids], "z": the map's JSON gzipped and base64'd}. The map is decoded once, on the first
    // popup, never at load; the id list says which links open a segment without decoding, so the render script
    // binds links in the same tick as it draws. A page that sets window.__iflowSegments itself (built by hand, or
    // by an older emitter) is read as it was. No element and no global: a map with no segment.
    var index = null;
    function readIndex() {
        if (index) return index;
        var el = document.getElementById('iflow-segments');
        var payload = {};
        if (el) {
            try { payload = JSON.parse(el.textContent) || {}; } catch (e) { payload = {}; }
        }
        index = {
            present: !!el,
            has: payload.has ? new Set(payload.has) : null,
            hidden: new Set(payload.hidden || []),
            z: payload.z || null
        };
        return index;
    }
    function legacySegments() {
        var legacy = window.__iflowSegments;
        return legacy && typeof legacy === 'object' ? legacy : null;
    }
    var segments = null;
    function loadSegments() {
        if (segments) return segments;
        var legacy = legacySegments();
        if (legacy) return (segments = Promise.resolve(legacy));
        var z = readIndex().z;
        if (!z) return (segments = Promise.resolve({}));
        segments = window.decompressGzipBase64(z).then(function(json) { return JSON.parse(json); });
        return segments;
    }
    function hasSegment(segmentId) {
        var legacy = legacySegments();
        if (legacy) return !!legacy[segmentId];
        var ix = readIndex();
        if (!ix.present) return false;
        return ix.has ? ix.has.has(segmentId) : !ix.hidden.has(segmentId);
    }
    window._iflowLoadSegments = loadSegments;
    window._iflowHasSegment = hasSegment;

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

        // The popup is in the page before anything renders in it: the render shim finds its target by id when it is
        // called, and a diagram already in its cache is written at once, into whatever the id names then.
        var loading = document.createElement('div');
        loading.className = 'iflow-loading';
        loading.textContent = 'Loading…';
        popup.appendChild(loading);
        overlay.appendChild(popup);
        overlay.addEventListener('click', function(e) {
            if (e.target === overlay) overlay.remove();
        });
        document.body.appendChild(overlay);

        loadSegments().then(function(map) {
            if (!overlay.isConnected) return; // closed, or replaced by another popup, while the map decoded
            loading.remove();
            fill(popup, map[segmentId]);
        }, function(error) {
            if (!overlay.isConnected) return;
            loading.remove();
            var failed = document.createElement('div');
            failed.className = 'iflow-no-data iflow-load-failed';
            failed.textContent = 'Internal flow data could not be decompressed: '
                + (error && error.message ? error.message : String(error));
            popup.appendChild(failed);
            if (window.console && console.error) console.error('Kronikol: internal flow data could not be decompressed', error);
        });
    }

    function fill(popup, segment) {
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
    }

    // Expose for direct binding from the render script
    window._iflowShowPopup = showPopup;

    // A server-drawn SVG (Server or Local rendering, inline) keeps every link Kronikol wrote as an <a>, and the browser
    // engine's render script, which leaves a link with no segment at rest, never runs over it. So a link HideLink had
    // dropped the segment of opened a popup saying there was no data. Such a link loses its href here and is text again:
    // no pointer, nothing to open.
    var XLINK = 'http://www.w3.org/1999/xlink';
    function iflowId(a) {
        var href = a.getAttribute('xlink:href') || a.getAttributeNS(XLINK, 'href') || a.getAttribute('href') || '';
        return href.indexOf('#iflow-') === 0 ? href.substring(1) : null;
    }
    function unlink(a) {
        a.removeAttribute('href');
        a.removeAttribute('xlink:href');
        a.removeAttributeNS(XLINK, 'href');
    }
    function unlinkDead(root) {
        var links = (root || document).querySelectorAll('a');
        for (var i = 0; i < links.length; i++) {
            var id = iflowId(links[i]);
            if (id && !hasSegment(id)) unlink(links[i]);
        }
    }
    window._iflowUnlinkDead = unlinkDead;
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function() { unlinkDead(document); });
    else unlinkDead(document);

    // Fallback: document-level click handler (capture phase for IKVM/server SVG compatibility)
    document.addEventListener('click', function(e) {
        var el = e.target;
        while (el && el !== document) {
            if (el.localName === 'a') {
                var id = iflowId(el);
                if (id) {
                    e.preventDefault();
                    e.stopPropagation();
                    // A link added after the page loaded is checked when it is clicked.
                    if (hasSegment(id)) showPopup(id);
                    else unlink(el);
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