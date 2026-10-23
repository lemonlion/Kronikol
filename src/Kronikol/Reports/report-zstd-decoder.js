// The zstd decoder of a compressed report (plans/HTML_COMPRESSION_PLAN.md 4.2 and 4.8): the browser's own
// DecompressionStream('zstd') where it has one, else libzstd's decoder built to WebAssembly, else fzstd in plain
// JavaScript where the page's policy refuses WebAssembly. The same text runs on the page, in the worker that decodes
// the diagram sources and in the search worker, so it declares nothing at the top level but self.__kronikolZstd: a
// report script that declared the same name with let or const would fail after document.open, which keeps the
// window's global scope.
self.__kronikolZstd = self.__kronikolZstd || (function () {
    var WASM = '__KRONIKOL_ZSTD_WASM__';
    var wasmInstance = null, wasmFailed = false, nativeFailed = false;

    function fromBase64(b64) {
        if (typeof Uint8Array.fromBase64 === 'function') return Uint8Array.fromBase64(b64);
        var raw = atob(b64), bytes = new Uint8Array(raw.length);
        for (var i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
        return bytes;
    }

    // A stream built from the bytes, never read out of a Blob: in WebKit every read of a Blob inside a worker fails.
    function streamOf(bytes) {
        return new ReadableStream({ start: function (c) { c.enqueue(bytes); c.close(); } });
    }

    function checked(out, length, how) {
        if (out.length !== length) throw new Error(how + ' decoded ' + out.length + ' bytes where the page records ' + length);
        return out;
    }

    function decodeStream(bytes, length, format) {
        return new Response(streamOf(bytes).pipeThrough(new DecompressionStream(format))).arrayBuffer()
            .then(function (buf) { return checked(new Uint8Array(buf), length, format); });
    }

    function hasNative() {
        if (nativeFailed || typeof DecompressionStream !== 'function') return false;
        try { new DecompressionStream('zstd'); return true; } catch (e) { return false; }
    }

    function wasm() {
        if (wasmInstance) return Promise.resolve(wasmInstance);
        if (wasmFailed || typeof WebAssembly !== 'object') return Promise.reject(new Error('no WebAssembly'));
        // The module tells its host when its memory grows; every view here is made after the last malloc, so it
        // has nothing to do.
        var imports = { env: { emscripten_notify_memory_growth: function () { } } };
        return WebAssembly.instantiate(fromBase64(WASM), imports).then(function (result) {
            wasmInstance = { exports: result.instance.exports };
            return wasmInstance;
        }, function (e) { wasmFailed = true; throw e; });
    }

    // libzstd's ZSTD_decompress checks the frame's checksum and says so through its return value, which this glue
    // reads (zstddec's own decode() does not); a frame that is not what was written is an error, never other bytes.
    function decodeWasm(w, bytes, length) {
        var x = w.exports;
        var inPtr = x.malloc(bytes.length), outPtr = x.malloc(length || 1);
        try {
            if (!inPtr || !outPtr) throw new Error('zstd (wasm): out of memory for ' + length + ' bytes');
            new Uint8Array(x.memory.buffer).set(bytes, inPtr);
            var r = x.ZSTD_decompress(outPtr, length, inPtr, bytes.length);
            if (x.ZSTD_isError(r)) throw new Error('zstd (wasm): the frame does not decode (error ' + (-(r | 0)) + ')');
            return checked(new Uint8Array(x.memory.buffer).slice(outPtr, outPtr + (r >>> 0)), length, 'zstd (wasm)');
        } finally {
            if (inPtr) x.free(inPtr);
            if (outPtr) x.free(outPtr);
        }
    }

    var fzstd = (function () {
        var module = { exports: {} }, exports = module.exports, define;
        /*__KRONIKOL_FZSTD__*/
        return module.exports;
    })();

    function decodeFzstd(bytes, length) {
        return checked(fzstd.decompress(bytes, new Uint8Array(length)), length, 'zstd (fzstd)');
    }

    var api = {
        path: null,
        fromBase64: fromBase64,
        streamOf: streamOf,
        // Decodes one frame to its bytes. codec is 'zstd', or 'gzip' or 'none' (a page the export wrote), length the
        // decoded size the page records.
        decode: function (bytes, length, codec) {
            if (codec === 'gzip') return decodeStream(bytes, length, 'gzip');
            // A frame stored as it is: what the export writes where the browser has no CompressionStream.
            if (codec === 'none') return new Promise(function (resolve) { resolve(checked(bytes, length, 'the stored frame')); });
            if (hasNative()) {
                return decodeStream(bytes, length, 'zstd').then(function (out) { api.path = 'native'; return out; },
                    function () { nativeFailed = true; return api.decode(bytes, length, codec); });
            }
            return wasm().then(function (w) {
                var out = decodeWasm(w, bytes, length);
                api.path = 'wasm';
                return out;
            }, function () {
                var out = decodeFzstd(bytes, length);
                api.path = 'fzstd';
                return out;
            });
        },
        // Lets the wasm instance and its memory go (the page frame is decoded once).
        release: function () { wasmInstance = null; }
    };
    return api;
})();
