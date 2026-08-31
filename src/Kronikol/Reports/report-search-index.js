// Deep search ("search everything") — SEARCH_INDEX_PLAN client side.
//
// The report embeds a hashed-trigram -> scenario-bitset index over the full corpus (note
// payloads, diagram text, SQL, flame spans, example values) in #kron-search-index. This script:
//   1. PURE SECTION — dependency-free functions (normalization, FNV-1a trigrams, index decode,
//      candidate over-approximation, deep matching). No DOM/worker/DecompressionStream
//      references: the Jint test suite executes them directly, pinned to the shared
//      cross-language vectors, and the Web Worker is built from these very functions via
//      Function.prototype.toString so exactly ONE copy of the logic exists.
//   2. MAIN-THREAD SECTION — worker plumbing, corpus metadata collection from the DOM, the
//      status chip, and integration with the existing sr-flag / applyVisibility pipeline.
//
// The index is an accelerator, never the truth: candidates are confirmed by running the same
// matcher the shallow search uses over the reassembled normalized corpus, so hash collisions
// only cost time and the advanced syntax composes unchanged. Deep results are authoritative
// (negated queries can remove shallow matches — the deep corpus is a superset).

// ===== PURE SECTION (Jint-tested; keep free of DOM/worker references) =====

// §4.1 shared normalization — must stay byte-identical to SearchNormalizer.cs and
// tools/search-bench/normalize.js (pinned by tests/shared-vectors/search-index-vectors.json).
function kronNormalizeForSearch(s) {
    s = s.replace(/\r\n/g, '\n');                                  // 1. canonicalize CRLF
    s = s.replace(/[A-Z]/g, function(c) { return c.toLowerCase(); }); // 2. ASCII-only fold
    s = s.replace(/~(?=[/*_\-"\[<#=])/g, '');                      // 3. creole escapes
    s = s.replace(/<\/?(?:color|font|i|b|size|back)[^>]*>/g, '');  // 4. markup tags
    s = s.replace(/\\n[ \t]*/g, '');                               // 5a. arrow-label literal \n escape
    // 5b. note-body rejoin (linear, parts joined once)
    var lines = s.split('\n');
    var parts = [];
    var inNote = false;
    for (var i = 0; i < lines.length; i++) {
        var l = lines[i];
        var trimmed = l.trim();
        if (/^note (left|right|over)/.test(trimmed)) { inNote = true; if (parts.length) parts.push('\n'); parts.push(l); continue; }
        if (trimmed === 'end note') { inNote = false; if (parts.length) parts.push('\n'); parts.push(l); continue; }
        if (inNote && parts.length && l.length > 0 && !/\s/.test(l[0])) {
            parts.push(l);
        } else {
            if (parts.length) parts.push('\n');
            parts.push(l);
        }
    }
    return (parts.join('') + '\n').replace(/[ \t]+/g, ' ');        // 6. collapse spaces
}

// Query text goes through the same normalization as the corpus (so creole/markup/whitespace
// behave identically), minus the corpus's trailing newline.
function kronNormalizeQueryText(input) {
    return kronNormalizeForSearch(input).replace(/\n+$/, '').trim();
}

// FNV-1a-32 over UTF-16 code units, bucket = hash & (B-1). Constants pinned by vectors.
function kronTrigramBuckets(term, bucketCount) {
    var out = [];
    var seen = {};
    for (var i = 0, n = term.length - 2; i < n; i++) {
        var h = 0x811c9dc5;
        h = Math.imul(h ^ term.charCodeAt(i), 0x01000193);
        h = Math.imul(h ^ term.charCodeAt(i + 1), 0x01000193);
        h = Math.imul(h ^ term.charCodeAt(i + 2), 0x01000193);
        var b = (h >>> 0) & (bucketCount - 1);
        if (!seen[b]) { seen[b] = true; out.push(b); }
    }
    return out;
}

// Decodes the KSI1 v1 binary layout (see SearchIndexBuilder.cs). Returns row OFFSETS, not
// materialized rows — rows are intersected lazily per query.
function kronDecodeSearchIndex(bytes) {
    if (bytes.length < 13 || bytes[0] !== 0x4b || bytes[1] !== 0x53 || bytes[2] !== 0x49 || bytes[3] !== 0x31)
        throw new Error('bad search index magic');
    if (bytes[4] !== 1) throw new Error('unsupported search index version ' + bytes[4]);
    var pos = 5;
    function u32() { var v = bytes[pos] | (bytes[pos + 1] << 8) | (bytes[pos + 2] << 16) | (bytes[pos + 3] << 24); pos += 4; return v >>> 0; }
    function varint() { var v = 0, sh = 0; while (bytes[pos] & 128) { v |= (bytes[pos++] & 127) << sh; sh += 7; } v |= bytes[pos++] << sh; return v >>> 0; }
    var B = u32();
    var docCount = u32();
    var anchors = new Array(docCount);
    for (var d = 0; d < docCount; d++) {
        var len = varint();
        var chunk = '';
        // UTF-8 decode (anchor ids are slugs, but stay correct for any content)
        var end = pos + len;
        while (pos < end) {
            var c = bytes[pos++];
            if (c < 128) chunk += String.fromCharCode(c);
            else if (c < 224) chunk += String.fromCharCode(((c & 31) << 6) | (bytes[pos++] & 63));
            else if (c < 240) chunk += String.fromCharCode(((c & 15) << 12) | ((bytes[pos++] & 63) << 6) | (bytes[pos++] & 63));
            else {
                var cp = ((c & 7) << 18) | ((bytes[pos++] & 63) << 12) | ((bytes[pos++] & 63) << 6) | (bytes[pos++] & 63);
                cp -= 0x10000;
                chunk += String.fromCharCode(0xD800 + (cp >> 10), 0xDC00 + (cp & 0x3FF));
            }
        }
        anchors[d] = chunk;
    }
    var offsets = new Int32Array(B); // offset of the payload (encoding byte); -1 = empty bucket
    for (var b = 0; b < B; b++) {
        var payloadLen = varint();
        offsets[b] = payloadLen === 0 ? -1 : pos;
        pos += payloadLen;
    }
    return { bytes: bytes, buckets: B, docCount: docCount, anchors: anchors, offsets: offsets, bitsetBytes: (docCount + 7) >> 3 };
}

// ANDs bucket row `b` into candidate bitset `out` (Uint8Array of ix.bitsetBytes).
function kronRowIntoBitset(ix, b, out) {
    var off = ix.offsets[b];
    var i;
    if (off < 0) { for (i = 0; i < out.length; i++) out[i] = 0; return; }
    var bytes = ix.bytes;
    var pos = off;
    var encoding = bytes[pos++];
    if (encoding === 1) {
        for (i = 0; i < out.length; i++) out[i] &= bytes[pos + i];
        return;
    }
    function varint() { var v = 0, sh = 0; while (bytes[pos] & 128) { v |= (bytes[pos++] & 127) << sh; sh += 7; } v |= bytes[pos++] << sh; return v >>> 0; }
    var count = varint();
    var tmp = new Uint8Array(out.length);
    var d = 0;
    for (i = 0; i < count; i++) {
        var v = varint();
        d = i === 0 ? v : d + v;
        tmp[d >> 3] |= 1 << (d & 7);
    }
    for (i = 0; i < out.length; i++) out[i] &= tmp[i];
}

// Candidate bitset for one normalized text term: AND across its trigram buckets.
// Terms shorter than 3 code units constrain nothing (all-ones).
function kronCandidateBitsetForTerm(ix, term) {
    var out = new Uint8Array(ix.bitsetBytes);
    var i;
    for (i = 0; i < out.length; i++) out[i] = 0xff;
    if (term.length < 3) return out;
    var buckets = kronTrigramBuckets(term, ix.buckets);
    for (i = 0; i < buckets.length; i++) kronRowIntoBitset(ix, buckets[i], out);
    return out;
}

// Over-approximating candidate docs for a query (§4.4): positive text/phrase terms use their
// candidate sets; tag/status terms and anything under negation never prune. Sound by
// construction — the verify pass computes the real answer.
function kronCandidateDocsForQuery(ix, input) {
    var out;
    function ones() { var o = new Uint8Array(ix.bitsetBytes); for (var i = 0; i < o.length; i++) o[i] = 0xff; return o; }
    function andInto(a, b) { for (var i = 0; i < a.length; i++) a[i] &= b[i]; return a; }
    function orInto(a, b) { for (var i = 0; i < a.length; i++) a[i] |= b[i]; return a; }
    function walk(ast) {
        switch (ast.type) {
            case 'text':
            case 'phrase':
                return kronCandidateBitsetForTerm(ix, kronNormalizeQueryText(ast.value));
            case 'and':
                return andInto(walk(ast.left), walk(ast.right));
            case 'or':
                return orInto(walk(ast.left), walk(ast.right));
            default: // tag, status, not — never prune
                return ones();
        }
    }
    var handled = false;
    if (isAdvancedSearch(input)) {
        var tokens = advancedSearchTokenise(input);
        if (tokens.length > 0) {
            var ast = advancedSearchParse(tokens);
            if (ast !== null) { out = walk(ast); handled = true; }
        }
    }
    if (!handled) {
        var split = splitLegacyTagExpression(input);
        var searchTokens = parseSearchTokensIncludingQuotes(split.textInput);
        out = ones();
        for (var t = 0; t < searchTokens.length; t++)
            andInto(out, kronCandidateBitsetForTerm(ix, kronNormalizeQueryText(searchTokens[t])));
    }
    var docs = [];
    for (var d = 0; d < ix.docCount; d++)
        if (out[d >> 3] & (1 << (d & 7))) docs.push(d);
    return docs;
}

// True when the query has at least one text/phrase term of >=3 normalized code units — the
// only queries the index can help with; everything else stays on the shallow path (§4.4).
function kronIsDeepEligible(input) {
    if (!input) return false;
    if (isAdvancedSearch(input)) {
        var tokens = advancedSearchTokenise(input);
        if (tokens.length > 0 && advancedSearchParse(tokens) !== null) {
            for (var i = 0; i < tokens.length; i++) {
                if ((tokens[i].type === 'text' || tokens[i].type === 'phrase')
                    && kronNormalizeQueryText(tokens[i].value || '').length >= 3) return true;
            }
            return false;
        }
    }
    var split = splitLegacyTagExpression(input);
    var searchTokens = parseSearchTokensIncludingQuotes(split.textInput);
    for (var t = 0; t < searchTokens.length; t++)
        if (kronNormalizeQueryText(searchTokens[t]).length >= 3) return true;
    return false;
}

// Deep match for one item: the EXISTING matcher semantics, run over the full normalized corpus.
// Corpus pieces are '\n'-joined — tokens and phrases never contain '\n', so matches can never
// span piece boundaries. Falls back to the legacy path exactly like run_search_scenarios does.
function kronDeepMatchesItem(input, deepInput, corpus, tags, status) {
    if (isAdvancedSearch(input)) {
        var result = advancedSearchMatch(deepInput, corpus, tags, status);
        if (result !== null) return result;
    }
    var split = splitLegacyTagExpression(deepInput);
    var searchTokens = parseSearchTokensIncludingQuotes(split.textInput);
    if (searchTokens.length === 0 && !split.tagExpr) return false;
    for (var j = 0; j < searchTokens.length; j++) {
        if (corpus.indexOf(searchTokens[j]) === -1) return false;
    }
    if (split.tagExpr) return evaluateTagExpression(split.tagExpr, tags);
    return true;
}

// Flame chart searchable text: sources, span names, marker labels, newline-joined in JSON
// order — the generator extracts the identical string (ReportGenerator.ExtractFlameSearchText).
function kronExtractFlameText(json) {
    var data = JSON.parse(json);
    var parts = [];
    var i;
    if (data.s) for (i = 0; i < data.s.length; i++) parts.push(data.s[i]);
    if (data.f) for (i = 0; i < data.f.length; i++) parts.push(data.f[i][1]);
    if (data.m) for (i = 0; i < data.m.length; i++) parts.push(data.m[i][1]);
    return parts.join('\n');
}

// ===== WORKER MAIN LOOP (runs inside the Blob worker; also pure — no page DOM) =====
// The Blob source is assembled from Function.prototype.toString of the pure functions above
// plus the shared query helpers, so the worker always runs the same logic the page (and the
// Jint tests) hold. Verify text is cached in a byte-bounded LRU (worker memory only — no
// browser storage by design).
function kronSearchWorkerMain(self) {
    var ix = null, items = null, pumlMap = null, docToItem = null;
    var latestGen = 0;
    var pendingQueries = [];
    var ready = false;
    var corpusCache = new Map(); // itemIndex -> normalized corpus string (LRU by re-insertion)
    var corpusCacheBytes = 0;
    var CORPUS_CACHE_LIMIT = 64 * 1024 * 1024;
    var BATCH_SIZE = 100;

    function b64ToBytes(b64) {
        var raw = atob(b64);
        var bytes = new Uint8Array(raw.length);
        for (var i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
        return bytes;
    }
    function gunzipToText(b64) {
        var stream = new Blob([b64ToBytes(b64)]).stream().pipeThrough(new DecompressionStream('gzip'));
        return new Response(stream).text();
    }
    function gunzipToBytes(b64) {
        var stream = new Blob([b64ToBytes(b64)]).stream().pipeThrough(new DecompressionStream('gzip'));
        return new Response(stream).arrayBuffer().then(function(buf) { return new Uint8Array(buf); });
    }
    function cachePut(i, corpus) {
        corpusCache.set(i, corpus);
        corpusCacheBytes += corpus.length * 2;
        while (corpusCacheBytes > CORPUS_CACHE_LIMIT && corpusCache.size > 1) {
            var oldest = corpusCache.keys().next().value;
            corpusCacheBytes -= corpusCache.get(oldest).length * 2;
            corpusCache.delete(oldest);
        }
    }
    async function corpusForItem(i) {
        var cached = corpusCache.get(i);
        if (cached !== undefined) {
            corpusCache.delete(i); corpusCache.set(i, cached); // refresh LRU position
            return cached;
        }
        var item = items[i];
        var parts = [kronNormalizeForSearch(item.searchText || '')];
        var k;
        for (k = 0; k < item.diagramIds.length; k++) {
            var z = pumlMap && pumlMap[item.diagramIds[k]];
            if (z) parts.push(kronNormalizeForSearch(await gunzipToText(z)));
        }
        for (k = 0; k < item.plantumlZ.length; k++)
            parts.push(kronNormalizeForSearch(await gunzipToText(item.plantumlZ[k])));
        for (k = 0; k < item.rawTexts.length; k++)
            parts.push(kronNormalizeForSearch(item.rawTexts[k]));
        for (k = 0; k < item.flameZ.length; k++)
            parts.push(kronNormalizeForSearch(kronExtractFlameText(await gunzipToText(item.flameZ[k]))));
        var corpus = parts.join('\n');
        cachePut(i, corpus);
        return corpus;
    }
    async function runQuery(msg) {
        var gen = msg.gen;
        var t0 = Date.now();
        var input = msg.input;
        var deepInput = kronNormalizeQueryText(input);
        var candDocs = kronCandidateDocsForQuery(ix, input);
        var candItemSet = {};
        var candItems = [];
        for (var i = 0; i < candDocs.length; i++) {
            var it = docToItem[candDocs[i]];
            if (it >= 0 && !candItemSet[it]) { candItemSet[it] = true; candItems.push(it); }
        }
        candItems.sort(function(a, b) { return a - b; });
        var matches = [];
        for (var c = 0; c < candItems.length; c++) {
            if (latestGen !== gen) return; // superseded — stop wasting work
            var idx = candItems[c];
            var corpus = await corpusForItem(idx);
            var item = items[idx];
            if (kronDeepMatchesItem(input, deepInput, corpus, new Set(item.tags), item.status))
                matches.push(idx);
            if (matches.length > 0 && matches.length % BATCH_SIZE === 0)
                self.postMessage({ type: 'result', gen: gen, done: false, matches: matches.slice() });
        }
        if (latestGen !== gen) return;
        self.postMessage({
            type: 'result', gen: gen, done: true, matches: matches,
            stats: { candidates: candDocs.length, candidateItems: candItems.length, verified: matches.length, ms: Date.now() - t0 }
        });
    }
    self.onmessage = function(e) {
        var msg = e.data;
        if (msg.type === 'init') {
            items = msg.items;
            pumlMap = msg.pumlDataJson ? JSON.parse(msg.pumlDataJson) : null;
            gunzipToBytes(msg.indexB64).then(function(bytes) {
                ix = kronDecodeSearchIndex(bytes);
                self.postMessage({ type: 'anchors', anchors: ix.anchors, buckets: ix.buckets, docCount: ix.docCount });
            });
        } else if (msg.type === 'docmap') {
            docToItem = msg.docToItem;
            ready = true;
            var q = pendingQueries; pendingQueries = [];
            for (var i = 0; i < q.length; i++) runQuery(q[i]);
        } else if (msg.type === 'query') {
            latestGen = msg.gen;
            if (!ready) { pendingQueries.push(msg); return; }
            runQuery(msg);
        }
    };
}

// ===== MAIN-THREAD SECTION (worker plumbing, chip, sr-flag integration) =====
(function() {
    if (typeof window === 'undefined' || typeof document === 'undefined') return; // worker/Jint context

    var state = {
        gen: 0,
        worker: null,
        initStarted: false,
        ready: false,
        shallowHidden: null,   // sr snapshot after the shallow pass, for the +N/-M chip counts
        chipTimer: null,
        chip: null
    };

    window.__kronikolSearch = { indexState: 'unknown', docs: 0, buckets: 0, lastQuery: null };

    function indexScript() { return document.getElementById('kron-search-index'); }

    function chipEl() {
        if (state.chip) return state.chip;
        var box = document.querySelector('.filter-search');
        if (!box) return null;
        var chip = document.createElement('span');
        chip.className = 'kron-deep-chip';
        chip.style.display = 'none';
        box.appendChild(chip);
        state.chip = chip;
        return chip;
    }
    function hideChip() {
        if (state.chipTimer) { clearTimeout(state.chipTimer); state.chipTimer = null; }
        var chip = chipEl();
        if (chip) { chip.style.display = 'none'; chip.classList.remove('kron-deep-chip-working'); }
    }
    function showChipWorkingSoon() {
        if (state.chipTimer) clearTimeout(state.chipTimer);
        // 0.2s delay, same pattern as the report's pending spinner — fast queries never flash it
        state.chipTimer = setTimeout(function() {
            var chip = chipEl();
            if (!chip) return;
            chip.textContent = 'searching everything…';
            chip.classList.add('kron-deep-chip-working');
            chip.style.display = '';
        }, 200);
    }
    function showChipDone(added, removed) {
        if (state.chipTimer) { clearTimeout(state.chipTimer); state.chipTimer = null; }
        var chip = chipEl();
        if (!chip) return;
        chip.classList.remove('kron-deep-chip-working');
        if (removed > 0) chip.textContent = 'results refined (+' + added + '/−' + removed + ')';
        else if (added > 0) chip.textContent = '+' + added + ' more found in payloads & diagrams';
        else chip.textContent = 'no additional matches';
        chip.style.display = '';
    }

    function collectItemMeta() {
        var c = fc();
        var items = [];
        for (var i = 0; i < c.items.length; i++) {
            var el = c.items[i].el;
            var tags = [];
            var cats = (el.getAttribute('data-categories') || '').toLowerCase();
            var labels = (el.getAttribute('data-labels') || '').toLowerCase();
            if (cats) cats.split(',').forEach(function(t) { tags.push(t.trim()); });
            if (labels) labels.split(',').forEach(function(t) { tags.push(t.trim()); });
            var diagramIds = [];
            el.querySelectorAll('.plantuml-browser[id], .plantuml-inline-svg[id]').forEach(function(d) {
                // #puml-data is written once and never mutated; data-plantuml attributes are
                // rewritten on first render, so ids into the immutable map are the only safe source
                if (!d.hasAttribute('data-plantuml-z')) diagramIds.push(d.id);
            });
            var plantumlZ = [];
            el.querySelectorAll('[data-plantuml-z]').forEach(function(d) { plantumlZ.push(d.getAttribute('data-plantuml-z')); });
            var rawTexts = [];
            el.querySelectorAll('.raw-plantuml pre').forEach(function(p) { rawTexts.push(p.textContent); });
            var flameZ = [];
            el.querySelectorAll('.iflow-flame[data-flame-z]').forEach(function(f) { flameZ.push(f.getAttribute('data-flame-z')); });
            items.push({
                searchText: c.items[i].searchText,
                tags: tags,
                status: c.items[i].status,
                diagramIds: diagramIds,
                plantumlZ: plantumlZ,
                rawTexts: rawTexts,
                flameZ: flameZ
            });
        }
        return items;
    }

    function buildWorker() {
        var fns = [
            kronNormalizeForSearch, kronNormalizeQueryText, kronTrigramBuckets,
            kronDecodeSearchIndex, kronRowIntoBitset, kronCandidateBitsetForTerm,
            kronCandidateDocsForQuery, kronIsDeepEligible, kronDeepMatchesItem,
            kronExtractFlameText,
            isAdvancedSearch, advancedSearchTokenise, advancedSearchParse,
            advancedSearchEvaluate, advancedSearchMatch,
            splitLegacyTagExpression, parseSearchTokensIncludingQuotes, evaluateTagExpression,
            kronSearchWorkerMain
        ];
        var src = fns.map(function(f) { return f.toString(); }).join('\n\n') + '\n\nkronSearchWorkerMain(self);\n';
        var url = URL.createObjectURL(new Blob([src], { type: 'application/javascript' }));
        var worker = new Worker(url);
        URL.revokeObjectURL(url);
        return worker;
    }

    function resolveDocMap(anchors) {
        var c = fc();
        var elToIndex = new Map();
        for (var i = 0; i < c.items.length; i++) elToIndex.set(c.items[i].el, i);
        var docToItem = new Array(anchors.length);
        for (var d = 0; d < anchors.length; d++) {
            var el = document.getElementById(anchors[d]);
            var idx = -1;
            while (el) {
                if (elToIndex.has(el)) { idx = elToIndex.get(el); break; }
                el = el.parentElement;
            }
            docToItem[d] = idx;
        }
        return docToItem;
    }

    function ensureInit() {
        if (state.initStarted) return;
        state.initStarted = true;
        var script = indexScript();
        if (!script || typeof DecompressionStream === 'undefined' || typeof Worker === 'undefined') {
            window.__kronikolSearch.indexState = script ? 'unsupported' : 'absent';
            return;
        }
        window.__kronikolSearch.indexState = 'loading';
        var indexB64;
        try { indexB64 = JSON.parse(script.textContent); }
        catch (e) { window.__kronikolSearch.indexState = 'error'; return; }
        var pumlScript = document.getElementById('puml-data');
        state.worker = buildWorker();
        state.worker.onmessage = function(e) {
            var msg = e.data;
            if (msg.type === 'anchors') {
                window.__kronikolSearch.docs = msg.docCount;
                window.__kronikolSearch.buckets = msg.buckets;
                state.worker.postMessage({ type: 'docmap', docToItem: resolveDocMap(msg.anchors) });
                state.ready = true;
                window.__kronikolSearch.indexState = 'ready';
            } else if (msg.type === 'result') {
                onDeepResult(msg);
            }
        };
        state.worker.onerror = function() {
            window.__kronikolSearch.indexState = 'error';
            hideChip();
        };
        state.worker.postMessage({
            type: 'init',
            indexB64: indexB64,
            pumlDataJson: pumlScript ? pumlScript.textContent : null,
            items: collectItemMeta()
        });
    }

    function onDeepResult(msg) {
        if (msg.gen !== state.gen) return; // stale — a newer query superseded it
        var c = fc();
        var matched = new Set(msg.matches);
        var changed = false;
        if (!msg.done) {
            // progressive reveal: batches only ADD results (document order); the authoritative
            // set (which may also remove) lands with the final message
            for (var i = 0; i < c.items.length; i++) {
                if (matched.has(i) && c.items[i].sr) { c.items[i].sr = false; changed = true; }
            }
            if (changed) applyVisibility(c);
            return;
        }
        var added = 0, removed = 0;
        for (var j = 0; j < c.items.length; j++) {
            var newSr = !matched.has(j);
            if (c.items[j].sr !== newSr) { c.items[j].sr = newSr; changed = true; }
            if (state.shallowHidden) {
                if (state.shallowHidden[j] && !newSr) added++;
                if (!state.shallowHidden[j] && newSr) removed++;
            }
        }
        if (changed) applyVisibility(c);
        showChipDone(added, removed);
        window.__kronikolSearch.lastQuery = {
            candidates: msg.stats.candidates,
            candidateItems: msg.stats.candidateItems,
            verified: msg.stats.verified,
            added: added,
            removed: removed,
            ms: msg.stats.ms
        };
    }

    // Called by run_search_scenarios after every shallow pass (input already lowercased+trimmed).
    window._kronDeepQuery = function(input) {
        state.gen++;
        if (!indexScript()) return;
        if (!kronIsDeepEligible(input)) { hideChip(); return; }
        ensureInit();
        if (window.__kronikolSearch.indexState === 'absent'
            || window.__kronikolSearch.indexState === 'unsupported'
            || window.__kronikolSearch.indexState === 'error') return;
        var c = fc();
        var shallowHidden = new Array(c.items.length);
        for (var i = 0; i < c.items.length; i++) shallowHidden[i] = c.items[i].sr;
        state.shallowHidden = shallowHidden;
        showChipWorkingSoon();
        state.worker.postMessage({ type: 'query', gen: state.gen, input: input });
    };

    window._kronDeepReset = function() {
        state.gen++;
        state.shallowHidden = null;
        hideChip();
    };
})();
