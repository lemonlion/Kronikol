// The loader of a compressed report (plans/HTML_COMPRESSION_PLAN.md 4.2). The file holds the report as zstd frames in
// base64: the page, then the diagram sources in pieces. This paints the loading screen, decodes the page frame, puts the
// report in place of this page with document.open, write and close, and decodes a piece of diagram sources the first
// time a diagram in it is asked for. It is one function expression: the window and its global scope outlive
// document.open, so a top-level let, const or class here would stop any report script that declared the same name.
// What the report needs from it is a property of window: __kronikolFrames (the pieces and how to read a diagram from
// them) and __kronikolLoader (this page's parts and sources, for the export).
(function () {
    var Z = self.__kronikolZstd;
    var loading = document.getElementById('kron-loading');
    var me = document.currentScript;

    function fail(err) {
        var text = (err && err.message) ? err.message : String(err);
        if (window.__kronikolLoad) window.__kronikolLoad.failure = text;
        if (window.console && console.error) console.error('Kronikol report: ' + text);
        if (!loading) return;
        // The hint's command names this file (in an export, the file the export downloads).
        var hint = loading.querySelector('.kron-loading-hint code');
        var command = hint ? hint.textContent : 'kronikol decode <this file> -o plain.html';
        loading.setAttribute('data-failed', '');
        loading.textContent = '';
        var h = document.createElement('p');
        h.textContent = 'This report could not be opened: ' + text + '.';
        var p = document.createElement('p');
        p.textContent = 'A plain copy of it can be written with the kronikol tool: ' + command;
        loading.appendChild(h);
        loading.appendChild(p);
    }

    function frameOf(el) {
        return {
            bytes: Z.fromBase64(el.textContent),
            codec: el.getAttribute('data-codec') || 'zstd',
            length: Number(el.getAttribute('data-length'))
        };
    }

    // The report's text, checked as UTF-8 and cut into strings of at most 4 MB of bytes each, so no single string
    // nears an engine's limit however large the page.
    function textOf(bytes) {
        var td = new TextDecoder('utf-8', { fatal: true });
        var parts = [], step = 4 << 20;
        for (var i = 0; i < bytes.length; i += step)
            parts.push(td.decode(bytes.subarray(i, Math.min(bytes.length, i + step)), { stream: true }));
        parts.push(td.decode());
        return parts;
    }

    // ---- the diagram sources ----
    var pieces = [], index = [], pieceOf = [], startOf = [], endOf = [];
    var cache = [], workerState = null;

    // The decoder's text, kept at the start: its element goes with the loader page.
    var decoderSource = '';

    function workerSource() {
        return decoderSource + '\n;(' + function () {
            self.onmessage = function (e) {
                var m = e.data;
                self.__kronikolZstd.decode(new Uint8Array(m.bytes), m.length, m.codec).then(function (out) {
                    self.postMessage({ id: m.id, bytes: out.buffer, path: self.__kronikolZstd.path }, [out.buffer]);
                }, function (err) {
                    self.postMessage({ id: m.id, error: (err && err.message) ? err.message : String(err) });
                });
            };
        } + ')();';
    }

    function decodeOnPage(piece) {
        load.pieceDecodesOnPage++;
        return Z.decode(piece.bytes, piece.length, piece.codec);
    }

    function decodeInWorker(piece) {
        if (!workerState) {
            workerState = { worker: null, failed: false, next: 1, pending: {} };
            try {
                var url = URL.createObjectURL(new Blob([workerSource()], { type: 'text/javascript' }));
                workerState.worker = new Worker(url);
                workerState.worker.onmessage = function (e) {
                    var p = workerState.pending[e.data.id];
                    if (!p) return;
                    delete workerState.pending[e.data.id];
                    if (e.data.error) p.reject(new Error(e.data.error));
                    else { load.workerPath = e.data.path; p.resolve(new Uint8Array(e.data.bytes)); }
                };
                // A policy that refuses blob: workers fails the worker after the fact: decode on the page from then on.
                workerState.worker.onerror = function (e) {
                    if (e && e.preventDefault) e.preventDefault();
                    workerState.failed = true;
                    var pending = workerState.pending;
                    workerState.pending = {};
                    Object.keys(pending).forEach(function (id) {
                        decodeOnPage(pending[id].piece).then(pending[id].resolve, pending[id].reject);
                    });
                };
            } catch (e) {
                workerState.failed = true;
            }
        }
        if (workerState.failed) return decodeOnPage(piece);
        return new Promise(function (resolve, reject) {
            var id = workerState.next++;
            workerState.pending[id] = { resolve: resolve, reject: reject, piece: piece };
            var copy = piece.bytes.slice();
            workerState.worker.postMessage({ id: id, bytes: copy.buffer, length: piece.length, codec: piece.codec }, [copy.buffer]);
        });
    }

    // At most two pieces are kept decoded; the one asked for last is kept first.
    function piece(p) {
        for (var i = 0; i < cache.length; i++) {
            if (cache[i].p === p) {
                var hit = cache.splice(i, 1)[0];
                cache.unshift(hit);
                return hit.promise;
            }
        }
        if (!pieces[p]) return Promise.reject(new Error('the diagram sources of piece ' + (p + 1) + ' are not in this file'));
        load.pieceDecodes++;
        var entry = { p: p, promise: decodeInWorker(pieces[p]) };
        entry.promise.catch(function () {
            var at = cache.indexOf(entry);
            if (at >= 0) cache.splice(at, 1);
        });
        cache.unshift(entry);
        if (cache.length > 2) cache.length = 2;
        return entry.promise;
    }

    function diagram(n) {
        if (!(n >= 0 && n < pieceOf.length)) return Promise.reject(new Error('no diagram ' + n + ' in this report'));
        return piece(pieceOf[n]).then(function (bytes) {
            return new TextDecoder('utf-8').decode(bytes.subarray(startOf[n], endOf[n]));
        });
    }

    // What the loader did and when (performance.now()), for a reader's console and the suites.
    var load = window.__kronikolLoad = {
        started: performance.now(), painted: null, decoded: null, written: null,
        pagePath: null, workerPath: null, pieceDecodes: 0, pieceDecodesOnPage: 0, failure: null
    };

    var frames = {
        pieces: pieces,
        index: index,
        // The piece a diagram's source is in, or -1 for a number the index does not hold.
        pieceOfDiagram: function (n) { return n >= 0 && n < pieceOf.length ? pieceOf[n] : -1; },
        // A value from the page: a diagram token (U+0001 and the diagram's number) is read from its piece; anything else
        // is the item's own text.
        item: function (value) {
            if (typeof value === 'string' && value.charCodeAt(0) === 1) return diagram(Number(value.slice(1)));
            return Promise.resolve(value);
        },
        diagram: diagram,
        piece: piece
    };

    function start() {
        var pageEl = document.getElementById('kron-frame-page');
        if (!pageEl) throw new Error('the page frame is missing');
        var page = frameOf(pageEl);
        // Each piece carries its number, so an export that copies only the pieces it needs keeps every token's place.
        var els = document.querySelectorAll('script[data-kron-piece]');
        for (var i = 0; i < els.length; i++) pieces[Number(els[i].getAttribute('data-kron-piece')) - 1] = frameOf(els[i]);
        var indexEl = document.getElementById('kron-diagram-index');
        var lengths = indexEl ? JSON.parse(indexEl.textContent) : [];
        for (var p = 0; p < lengths.length; p++) {
            index.push(lengths[p]);
            var at = 0;
            for (var k = 0; k < lengths[p].length; k++) {
                pieceOf.push(p); startOf.push(at); at += lengths[p][k]; endOf.push(at);
            }
        }
        decoderSource = document.getElementById('kron-zstd').textContent;
        window.__kronikolLoader = {
            loader: me ? me.textContent : '',
            decoder: decoderSource,
            head: document.head.innerHTML,
            loading: loading ? loading.outerHTML : '',
            notice: (document.querySelector('noscript') || { outerHTML: '' }).outerHTML
        };
        return Z.decode(page.bytes, page.length, page.codec).then(function (bytes) {
            load.decoded = performance.now();
            load.pagePath = page.codec === 'zstd' ? Z.path : page.codec;
            Z.release();
            var parts = textOf(bytes);
            bytes = null;
            window.__kronikolFrames = frames;
            document.open();
            // Added after document.open, which erases every listener added before it; it runs before the report's own.
            document.addEventListener('DOMContentLoaded', function () {
                window.__kronikolReady = true;
                document.dispatchEvent(new Event('kronikol:ready'));
            });
            for (var i = 0; i < parts.length; i++) { document.write(parts[i]); parts[i] = null; }
            document.close();
            load.written = performance.now();
        });
    }

    // Paint the loading screen first; a tab that is not shown gets no animation frame, so a timer goes too.
    var started = false;
    function go() {
        if (started) return;
        started = true;
        load.painted = performance.now();
        try { start().catch(fail); } catch (e) { fail(e); }
    }
    requestAnimationFrame(function () { setTimeout(go, 0); });
    setTimeout(go, 250);
})();
