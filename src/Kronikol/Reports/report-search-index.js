// Deep search ("search everything") — SEARCH_INDEX_PLAN client side.
//
// The report embeds a hashed-trigram -> scenario-bitset index over the full corpus (note
// payloads, diagram text, SQL, flame spans, example values) in #kron-search-index. This script:
//   1. PURE SECTION — dependency-free functions (normalization, FNV-1a trigrams, index decode,
//      candidate over-approximation, deep matching, where a match is). No DOM/worker/DecompressionStream
//      references: the Jint test suite executes them directly, pinned to the shared
//      cross-language vectors, and the Web Worker is built from these very functions via
//      Function.prototype.toString so exactly ONE copy of the logic exists.
//   2. MAIN-THREAD SECTION — worker plumbing, corpus metadata collection from the DOM, the
//      status chip, and integration with the existing sr-flag / applyVisibility pipeline.
//
// The index is an accelerator, never the truth: candidates are confirmed by running the same
// matcher the shallow search uses over the reassembled normalized corpus, so hash collisions
// only cost time and the advanced syntax composes unchanged. Deep results are authoritative
// only for negated (!!) queries, which can remove shallow matches — the deep corpus is a
// superset, so seeing more text can turn `!! term` false. Positive queries are strictly
// add-only (kronQueryCanRemove).

// ===== PURE SECTION (Jint-tested; keep free of DOM/worker references) =====

// §4.1 shared normalization — must stay byte-identical to SearchNormalizer.cs and
// tools/search-bench/normalize.js (pinned by tests/shared-vectors/search-index-vectors.json).
// Its passes are one list, which the search text, the display text and the map back to the input are each made from
// (plans/SEARCH_PLAN.md 4.6.3), so a rule changed here reaches all three. A pass is a regex and what it writes in place
// of each match: a function, called as replace() calls one, or a text written as it is (no $ patterns). `corpus` marks a
// pass a query skips, `fold` the ASCII fold, `has` a text without which the pass finds nothing, `tail` a text added to
// the end before the pass runs. The markers are literals INSIDE this function, not shared module-scope vars:
// buildWorker serialises each listed function with toString() and nothing else travels with it.
function kronNormalizerPasses() {
    return [
        { re: /\r\n/g, to: '\n' },                                     // 1. canonicalize CRLF
        // 1b. Undo exactly the note-body breaks the generator marked (NOTE_COPY_FIDELITY_PLAN): a line ending in its
        // <U+200B> joins the next, one ending in two joins it with a space, and a marker with `~` in front is the
        // payload's own text. Replaced a flush-left heuristic that welded together any note line starting at column 0
        // — right for indented JSON, wrong for plain text and SQL. Runs before the fold and the creole-escape strip
        // because pass 3 removes the `~` that tells Kronikol's marker from a payload's own "<U+200B>". The character
        // before the marker is matched and written back, which a lookbehind would only look at.
        { re: /(^|[^~])(<U\+200B>(?:<U\+200B>)?)\n/gm, has: '<U+200B>', to: function(m, before, marks) {
            return before + (marks.length > 8 ? ' ' : '');
        } },
        // 3. (3.30.1) The generator writes captured text that PlantUML's preprocessor or creole would act on as a
        // code point, <U+hhhh>, which the engine paints as the character. Creole escapes and code points are read in
        // one left-to-right pass, before the fold: `~<U+0027>` is an escaped `<` and then the payload's own text, a
        // decoded `~` escapes nothing, and a decoded letter folds like any other. A zero-width space paints nothing
        // and is dropped; a code point that is no character is left as written. A query skips this rule: it holds
        // none of the generator's escapes, and its tildes are what the reader saw.
        { re: /~([/*_\-"\[<#=])|<U\+([0-9A-Fa-f]{4,6})>/g, corpus: true, to: function(m, escaped, hex) {
            if (escaped !== undefined) return escaped;
            var cp = parseInt(hex, 16);
            if (cp === 0x200b) return '';
            if ((cp >= 0xd800 && cp <= 0xdfff) || cp > 0x10ffff) return m;
            return String.fromCodePoint(cp);
        } },
        { re: /[A-Z]/g, fold: true, to: function(c) { return c.toLowerCase(); } }, // 2. ASCII-only fold
        // The passes after the fold match any case and write no letter, so the fold changes nothing they do: the display
        // text is the text made without it (kronNormalizeRun makes it last).
        // 4. markup tags, and a step table cell's own (5.9.1, plans/REPORT_UI_FIXES_PLAN.md B2): <space:n>, <sub>, <sup>
        { re: /<\/?(?:color|font|i|b|size|back)[^>]*>|<space:[0-9]+>|<\/?su[pb]>/gi, to: '' },
        // 5. a table cell's line break (5.9.1, B3): a cell's \\n is a break of its text
        { re: /\\\\n/gi, to: '\n' },
        { re: /\\n[ \t]*/gi, to: '' },                                  // 5a. arrow-label literal \n escape
        { re: /[ \t]{2,}|\t/g, tail: '\n', to: ' ' }                    // 6. collapse spaces (the runs it changes)
    ];
}

// The search text: every pass, as the vectors pin it; a query (`query`) skips the passes for the corpus alone.
function kronNormalizeForSearch(s, query) {
    var passes = kronNormalizerPasses();
    for (var i = 0; i < passes.length; i++) {
        var p = passes[i];
        if (query && p.corpus) continue;
        if (p.tail) s += p.tail;
        if (!p.has || s.indexOf(p.has) >= 0) s = s.replace(p.re, p.to);
    }
    return s;
}

// The passes, keeping track of where what they keep goes (plans/SEARCH_PLAN.md 4.6.2, 4.6.3 and 4.6.5). The fold is made
// last, which changes nothing the passes after it do (kronNormalizerPasses), and the text before it is the display text.
// Each pass's matches are found as kronNormalizeForSearch finds them, and made as edits: a character a pass keeps stays
// itself, and what a pass writes in place of a match came from the whole match, but for the start of the match it writes
// back as it was (1b's character before its marker, the first space of a run). Asked for:
//   bounds  a sorted array of places in s, each between two characters: each moved to the same place in the text, a
//           place inside a match to where what the pass writes in its place starts; or 'lines', each line's start
//   cases   the places in the text of the letters the fold lowered
//   runs    the map back to s: four numbers a run of the text, its start and end, then the start and end in s of what it
//           came from; a run as long as what it came from maps character to character, any other as a whole
// Returns { text, display, bounds, cases, runs }: text is kronNormalizeForSearch(s, query) and display the text before
// the fold; what was not asked for is null. A pass is a replace() while nothing is followed through it: each line's start
// is found after the line break before it while the passes keep every one, and followed from the first pass that does
// not. A pass takes line breaks out or puts them in, never both, and one that leaves their number keeps each in place.
function kronNormalizeRun(s, query, bounds, cases, runs) {
    var passes = kronNormalizerPasses(), inLength = s.length, fold = null;
    var map = runs ? (s.length ? [0, s.length, 0, s.length] : []) : null;
    var lines = bounds === 'lines' ? count(s) : -1, breaks = lines;
    bounds = lines >= 0 ? null : bounds ? bounds.slice() : null;
    for (var p = 0; p < passes.length; p++) {
        var pass = passes[p];
        if (query && pass.corpus) continue;
        if (pass.fold) { fold = pass; continue; }
        if (pass.tail) {
            if (map) map.push(s.length, s.length + pass.tail.length, inLength, inLength);
            s += pass.tail;
            if (breaks >= 0) breaks += count(pass.tail);
        }
        if (pass.has && s.indexOf(pass.has) < 0) continue;
        if (!bounds && !map) {
            var next = s.replace(pass.re, pass.to);
            if (breaks < 0 || next === s || count(next) === breaks) { s = next; continue; }
            bounds = starts(s, lines);
            breaks = -1;
        }
        var edits = edit(s, pass);
        if (edits.length === 0) continue;
        if (bounds) moveBounds(bounds, edits);
        if (map) map = compose(map, edits, s);
        var out = '', at = 0;
        for (var e = 0; e < edits.length; e += 4) { out += s.slice(at, edits[e]) + edits[e + 2]; at = edits[e + 1]; }
        s = out + s.slice(at);
    }
    if (breaks >= 0) bounds = starts(s, lines);
    // one character for one, so nothing moves; the letters it lowers are where the display text differs
    var display = s, letters = cases ? [] : null;
    if (fold) s = letters ? s.replace(fold.re, function(c, at) { letters.push(at); return fold.to(c); }) : s.replace(fold.re, fold.to);
    return { text: s, display: display, bounds: bounds, cases: letters, runs: map };

    function count(text) {
        var n = 0;
        for (var i = text.indexOf('\n'); i >= 0; i = text.indexOf('\n', i + 1)) n++;
        return n;
    }
    // the start of the text and the place after each of its first n line breaks
    function starts(text, n) {
        var out = [0];
        for (var i = text.indexOf('\n'); i >= 0 && out.length <= n; i = text.indexOf('\n', i + 1)) out.push(i + 1);
        return out;
    }
    // the matches that change something, four entries each: start, end, what is written, and how much of the match's
    // start it writes back as it was
    function edit(text, pass) {
        var out = [], re = pass.re, call = typeof pass.to === 'function', m;
        re.lastIndex = 0;
        while ((m = re.exec(text)) !== null) {
            var match = m[0], at = m.index, written;
            if (call) { m.push(at, text); written = pass.to.apply(null, m); } else written = pass.to;
            if (written !== match) {
                var keep = 0;
                while (keep < written.length && keep < match.length && written.charCodeAt(keep) === match.charCodeAt(keep)) keep++;
                out.push(at, at + match.length, written, keep);
            }
            if (!re.global) break;
            if (match.length === 0) re.lastIndex++;
        }
        return out;
    }
    // places between characters: before a match as they were, inside what it writes back as they were, inside the rest
    // to where what it writes there starts, after it moved by what it changed in length
    function moveBounds(places, edits) {
        var e = 0, shift = 0;
        for (var i = 0; i < places.length; i++) {
            var x = places[i];
            while (e < edits.length && edits[e + 1] <= x) { shift += edits[e + 2].length - (edits[e + 1] - edits[e]); e += 4; }
            var kept = e < edits.length ? edits[e] + edits[e + 3] : x;
            places[i] = (e < edits.length && edits[e] < x && x > kept ? kept : x) + shift;
        }
    }
    // the map from the text the edits make: a stretch they leave as the map had it, moved; what a pass wrote to what its
    // match came from, or, written past a match it has used up, to where that match ends
    function compose(old, edits, text) {
        var out = [], k = 0, shift = 0, at = 0;
        function push(oS, oE, iS, iE) {
            var n = out.length;
            if (n && out[n - 3] === oS && out[n - 1] === iS && oE - oS === iE - iS && out[n - 3] - out[n - 4] === out[n - 1] - out[n - 2]) {
                out[n - 3] = oE; out[n - 1] = iE;
            } else out.push(oS, oE, iS, iE);
        }
        function seek(x) { while (k + 4 < old.length && old[k + 1] <= x) k += 4; }
        function one() { return old[k + 1] - old[k] === old[k + 3] - old[k + 2]; }
        function from(x) { if (x >= text.length) return inLength; seek(x); return one() ? old[k + 2] + x - old[k] : old[k + 2]; }
        function to(x) { seek(x); return one() ? old[k + 2] + x - old[k] + 1 : old[k + 3]; }
        function copy(x, y) {
            while (x < y) {
                seek(x);
                var end = y < old[k + 1] ? y : old[k + 1];
                if (one()) push(x + shift, end + shift, old[k + 2] + x - old[k], old[k + 2] + end - old[k]);
                else push(x + shift, end + shift, old[k + 2], old[k + 3]);
                x = end;
            }
        }
        for (var e = 0; e < edits.length; e += 4) {
            var a = edits[e], b = edits[e + 1], written = edits[e + 2], keep = edits[e + 3];
            copy(at, a + keep);
            if (written.length > keep) {
                var rest = a + keep < b, f = rest ? from(a + keep) : (b > a ? to(b - 1) : from(a));
                push(a + keep + shift, a + written.length + shift, f, rest ? to(b - 1) : f);
            }
            shift += written.length - (b - a);
            at = b;
        }
        copy(at, text.length);
        return out;
    }
}

// The display text (plans/SEARCH_PLAN.md 4.6.3): kronNormalizeForSearch's passes without the ASCII fold, the passes after
// it matching any case, so the two texts are one length and differ only in the case of ASCII letters, and a range found
// in the search text is the same range here.
function kronNormalizeForDisplay(s, query) {
    return kronNormalizeRun(s, query, null, false, false).display;
}

// The search text and the map back to s (plans/SEARCH_PLAN.md 4.6.5): { text, runs }, the runs as kronNormalizeRun keeps
// them. <U+0045> maps to e, ~* to *, a tab or a marked break to a space, and what a pass takes out to nothing.
function kronNormalizeWithMap(s, query) {
    var run = kronNormalizeRun(s, query, null, false, true);
    return { text: run.text, runs: run.runs };
}

// Where in the input a stretch of the text came from, by kronNormalizeWithMap's runs: [start, end), from the start of
// what its first character came from to the end of what its last did. An empty stretch is the place it is at.
function kronMapBack(runs, from, to) {
    var n = runs.length;
    if (!n) return [0, 0];
    function find(x) {
        var lo = 0, hi = n / 4 - 1;
        while (lo < hi) { var mid = (lo + hi + 1) >> 1; if (runs[mid * 4] <= x) lo = mid; else hi = mid - 1; }
        return lo * 4;
    }
    function one(k) { return runs[k + 1] - runs[k] === runs[k + 3] - runs[k + 2]; }
    function start(x) {
        if (x >= runs[n - 3]) return runs[n - 1];
        var k = find(x);
        return one(k) ? runs[k + 2] + x - runs[k] : runs[k + 2];
    }
    function end(x) {
        var k = find(x);
        return one(k) ? runs[k + 2] + x - runs[k] + 1 : runs[k + 3];
    }
    if (to <= from) { var p = start(from); return [p, p]; }
    return [start(from), end(to - 1)];
}

// Query text goes through the same normalization as the corpus (so markup and whitespace behave
// identically) except rule 3, minus the corpus's trailing newline.
function kronNormalizeQueryText(input) {
    return kronNormalizeForSearch(input, true).replace(/\n+$/, '').trim();
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

// True only for advanced queries containing !! — the one query class where seeing MORE text can
// turn a match into a non-match. Deep results may REMOVE a shallow match only for these; every
// other query is add-only, so a raw shallow match always survives (the shallow pass matches the
// raw data-search text while deep matches it normalized — for positive queries that asymmetry
// must never hide a result the user can already see).
function kronQueryCanRemove(input) {
    if (!input || !isAdvancedSearch(input)) return false;
    var tokens = advancedSearchTokenise(input);
    if (!tokens || tokens.length === 0 || advancedSearchParse(tokens) === null) return false;
    for (var i = 0; i < tokens.length; i++)
        if (tokens[i].type === 'not') return true;
    return false;
}

// Deep match for one item: the EXISTING matcher semantics, run over the full normalized corpus.
// Corpus pieces are '\n'-joined — tokens and phrases never contain '\n', so matches can never
// span piece boundaries. Falls back to the legacy path exactly like run_search_scenarios does.
// kron:verdicts
function kronDeepMatchesItem(input, deepInput, corpus, tags, status, verdicts) {
// kron:else
//|function kronDeepMatchesItem(input, deepInput, corpus, tags, status) {
// kron:/verdicts
    if (isAdvancedSearch(input)) {
// kron:verdicts
        var result = advancedSearchMatch(deepInput, corpus, tags, status, verdicts);
// kron:else
//|        var result = advancedSearchMatch(deepInput, corpus, tags, status);
// kron:/verdicts
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

// ----- Where it matched (plans/SEARCH_PLAN.md 4.6.2 to 4.6.5) -----

// A diagram source's note blocks, `note … left|right` to `end note`, by the rule the page counts notes by (the note menu,
// a note's states, collapsible-notes-script.js's parseNoteBlocks, which hands its source here): a note's index in this
// list is its original index, which a line of a result names it by.
function kronParseNoteBlocks(source, lines) {
    if (!source) return [];
    lines = lines || source.split('\n');
    var notes = [];
    for (var i = 0; i < lines.length; i++) {
        // a header holds the word and an end the two, which most lines do not: only those are trimmed and read
        if (lines[i].indexOf('note') < 0) continue;
        var trimmed = lines[i].trim();
        if (/^note(?:<<[^>]*>>)*\s+(left|right)/.test(trimmed)) {
            var start = i;
            i++;
            var contentLines = [];
            while (i < lines.length && (lines[i].indexOf('end note') < 0 || lines[i].trim() !== 'end note')) {
                contentLines.push(lines[i]);
                i++;
            }
            notes.push({ start: start, end: i, contentLines: contentLines });
        }
    }
    return notes;
}

// A diagram source's statements, the places a hit is named by: its note blocks (kronParseNoteBlocks), and each statement
// named by its line in the source: an arrow, a participant's declaration, a loop, group or partition label, an hnote (a
// block to its end note when its header has no colon, as another note over or across is), a delay, a divider, an
// activity's action or swimlane. Each is { what, line, last, note }: its kind, its first and last line (from 0) and its
// note index, or -1. What draws no text of a hit's is none: the header and its directives, a style or skinparam block,
// an end, a comment.
function kronDiagramStatements(source) {
    var lines = source ? source.split('\n') : [], notes = kronParseNoteBlocks(source, lines), out = [], n = 0, skip = null, open = null;
    for (var i = 0; i < lines.length; i++) {
        if (n < notes.length && notes[n].start === i) {
            var end = Math.min(notes[n].end, lines.length - 1);
            out.push({ what: 'note', line: i, last: end, note: n++ });
            i = end;
            continue;
        }
        var t = lines[i].trim(), m, what = null;
        if (skip) { if (skip.test(t)) skip = null; continue; }
        if (open) { if (/^end\s*[hr]?note$/i.test(t)) { open.last = i; open = null; } continue; }
        if (!t || /^['@!]/.test(t) || /^(?:end|autonumber|hide|show|activate|deactivate|destroy|return|title|scale|newpage)\b/i.test(t)) continue;
        if (/^<style\s*>/i.test(t)) { skip = /^<\/style>/i; continue; }
        if (/^skinparam\b/i.test(t)) { if (/\{$/.test(t)) skip = /^\}/; continue; }
        if ((m = /^([hr]?note)\b/i.exec(t))) what = m[1].toLowerCase();
        else if (/^(?:participant|actor|boundary|control|entity|database|collections|queue)\b/i.test(t)) what = 'participant';
        else if (/^loop\b/i.test(t)) what = 'loop';
        else if (/^(?:group|alt|else|opt|par|break|critical)\b/i.test(t)) what = 'group';
        else if (/^partition\b/i.test(t)) what = 'partition';
        else if (/^\.\.\./.test(t)) what = 'delay';
        else if (/^==/.test(t)) what = 'divider';
        else if (/^:/.test(t)) what = 'action';
        else if (/^\|.*\|$/.test(t)) what = 'swimlane';
        else if (/^[^:]*?(?:-(?:\[[^\]]*\])?-*>|<-)/.test(t)) what = 'arrow';
        if (!what) continue;
        var statement = { what: what, line: i, last: i, note: -1 };
        out.push(statement);
        if (m && t.indexOf(':') < 0) { open = statement; statement.last = lines.length - 1; }
    }
    return out;
}

// A piece of a scenario's text as the worker keeps it (4.6.2): its search text, where each place of `at` in the raw text
// went in it (each line's start when `at` is left out: a hit's row), and where the letters the fold lowered are in it.
function kronCorpusPiece(raw, at) {
    var run = kronNormalizeRun(raw, false, at || 'lines', true, false);
    return { text: run.text, rows: run.bounds, cases: run.cases };
}

// The words a match hits (4.6.2): the text and phrase terms of the normalized query that the deep matcher looks for and
// that no !! (or an odd number of them) stands before, each once, read as kronDeepMatchesItem reads the query: the
// advanced grammar where it parses, the legacy words where it does not.
function kronPositiveTerms(input, deepInput) {
    var out = [];
    function add(term) { if (term && out.indexOf(term) < 0) out.push(term); }
    function walk(node, negated) {
        if (node.type === 'text' || node.type === 'phrase') { if (!negated) add(node.value); }
        else if (node.type === 'not') walk(node.operand, !negated);
        else if (node.type === 'and' || node.type === 'or') { walk(node.left, negated); walk(node.right, negated); }
    }
    if (isAdvancedSearch(input)) {
        var tokens = advancedSearchTokenise(deepInput);
        var ast = tokens.length > 0 ? advancedSearchParse(tokens) : null;
        if (ast !== null) { walk(ast, false); return out; }
    }
    parseSearchTokensIncludingQuotes(splitLegacyTagExpression(deepInput).textInput).forEach(add);
    return out;
}

// The kinds of the parts of a scenario's text each positive term hit (4.6.2): a string of kind letters a term, in the
// order the text has them. The scenario's data-search has its own (f n d p r t e s g x, its data-search-kinds); a deep
// piece is g a diagram's source, e the failure's text, or w the whole-test flow.
function kronItemKinds(entry, terms) {
    var spans = [], text = entry.text, out = [];
    entry.pieces.forEach(function(p) {
        if (!p.kinds) { spans.push(p.start, p.end, p.kind); return; }
        for (var j = 0; j < p.kinds.length; j++)
            spans.push(p.kinds[j].at, j + 1 < p.kinds.length ? p.kinds[j + 1].at : p.end, p.kinds[j].kind);
    });
    for (var t = 0; t < terms.length; t++) {
        var found = '', s = 0, at = text.indexOf(terms[t]);
        while (at >= 0 && s < spans.length) {
            while (s < spans.length && spans[s + 1] <= at) s += 3;
            if (s >= spans.length) break;
            if (at < spans[s]) { at = text.indexOf(terms[t], spans[s]); continue; }
            if (found.indexOf(spans[s + 2]) < 0) found += spans[s + 2];
            at = text.indexOf(terms[t], spans[s + 1]);
        }
        out.push(found);
    }
    return out;
}

// The line of `text` holding a hit (hit to hit + length), as a result shows it (4.6.3): the whole line when it fits in
// `width` characters, else `width` around the hit, cut at word boundaries where one is near enough, with "…" where it
// is cut; spaces at either end of the line are left out. Returns { text, from, to, lead, tail }: the line as shown, the
// stretch of `text` it shows, and 1 for a "…" before it (lead) or after it (tail), so a place x of `text` between from
// and to is x - from + lead in the line.
function kronLineAround(text, hit, length, width) {
    var start = hit > 0 ? text.lastIndexOf('\n', hit - 1) + 1 : 0, end = text.indexOf('\n', hit), close = hit + length;
    if (end < 0) end = text.length;
    function space(i) { var c = text.charCodeAt(i); return c === 32 || c === 9 || c === 13; }
    while (start < hit && space(start)) start++;
    while (end > close && space(end - 1)) end--;
    var from = start, to = end;
    if (end - start > width) {
        if (length >= width) { from = hit; to = hit + width; }
        else {
            from = Math.max(start, hit - Math.floor((width - length) / 3));
            to = Math.min(end, from + width);
            from = Math.max(start, to - width);
            // a word cut at the start goes, and so does one cut at the end, but never into the hit
            if (from > start && !space(from - 1)) {
                var f = from;
                while (f < hit && !space(f)) f++;
                while (f < hit && space(f)) f++;
                if (f <= hit && space(f - 1)) from = f;
            }
            if (to < end && !space(to)) {
                var e = to;
                while (e > close && !space(e - 1)) e--;
                while (e > close && space(e - 1)) e--;
                if (e >= close && space(e)) to = e;
            }
            while (from < hit && space(from)) from++;
            while (to > close && space(to - 1)) to--;
        }
    }
    var lead = from > start ? 1 : 0, tail = to < end ? 1 : 0;
    return { text: (lead ? '…' : '') + text.slice(from, to) + (tail ? '…' : ''), from: from, to: to, lead: lead, tail: tail };
}

// The lines where a scenario's deep pieces hold the positive terms (4.6.2, 4.6.5), for a result shown: how many lines hold
// a hit (count), how many hits there are (hits), and up to `max` of the lines in the order of the text, each
//   { kind, sort, index, ref, place, what, row, n, text, marks }
// kind the piece's (g, e, w); sort, index and ref the element it came from: its sort (puml, z, raw, fail, flame), its
// place among those of the scenario, its id; place where it is as hit= writes it (n3 a note by its original index, l14
// a statement by its first line in the source, f12 a line of the flow, e the failure, x a Raw PlantUML section), or null
// where no statement holds it, and what the statement is (note, arrow, hnote, participant, partition…, flow, error,
// source); row the hit's line in the piece as written (from 0); n which hit of its place the line's first is (from 1);
// text the line as written, cut to 160 around its first hit (kronLineAround), as the letters the fold lowered (the
// entry's mask) give it back; marks the [start, end] of each hit in it. The scenario's data-search is not looked in: the
// page shows the lines of its kinds from its own markup (4.6.4).
function kronItemSnippets(entry, terms, max) {
    var text = entry.text, mask = entry.mask, hits = [], i, j;
    entry.pieces.forEach(function(p, k) {
        if (p.kind === 's') return;
        terms.forEach(function(term) {
            for (var at = text.indexOf(term, p.start); at >= 0 && at < p.end; at = text.indexOf(term, at + 1)) hits.push([at, at + term.length, k]);
        });
    });
    hits.sort(function(a, b) { return a[0] - b[0] || b[1] - a[1]; });
    // one mark a run of overlapping hits
    var marks = [];
    for (i = 0; i < hits.length; i++) {
        var last = marks[marks.length - 1];
        if (last && hits[i][0] < last[1]) last[1] = Math.max(last[1], hits[i][1]);
        else marks.push(hits[i].slice());
    }
    // the hits a line holds, a line at a time
    var lines = [];
    for (i = 0; i < marks.length; i++) {
        var lineEnd = text.indexOf('\n', marks[i][0]);
        var group = { first: marks[i], marks: [marks[i]] };
        while (i + 1 < marks.length && marks[i + 1][0] < lineEnd) group.marks.push(marks[++i]);
        lines.push(group);
    }
    function rowOf(rows, x) {
        var lo = 0, hi = rows.length - 1;
        while (lo < hi) { var mid = (lo + hi + 1) >> 1; if (rows[mid] <= x) lo = mid; else hi = mid - 1; }
        return lo;
    }
    function shown(from, to) {
        var part = text.slice(from, to);
        if (!mask) return part;
        var out = '', at = 0;
        for (var x = from; x < to; x++)
            if (mask[x >> 3] & (1 << (x & 7))) { out += part.slice(at, x - from) + part.charAt(x - from).toUpperCase(); at = x - from + 1; }
        return out + part.slice(at);
    }
    function hitsBetween(from, to) {
        var count = 0;
        for (var x = 0; x < marks.length && marks[x][0] < to; x++) if (marks[x][0] >= from) count++;
        return count;
    }
    var out = [];
    for (i = 0; i < lines.length && out.length < max; i++) {
        var hit = lines[i].first, p = entry.pieces[hit[2]], row = rowOf(p.rows, hit[0]);
        var place = null, what = null, from = p.start, to = p.end;
        if (p.kind === 'e') { place = 'e'; what = 'error'; }
        else if (p.kind === 'w') { place = 'f' + row; what = 'flow'; from = p.rows[row]; to = row + 1 < p.rows.length ? p.rows[row + 1] : p.end; }
        else if (p.sort === 'raw') { place = 'x'; what = 'source'; }
        else if (p.statements) {
            for (j = 0; j < p.statements.length && p.statements[j].line <= row; j++) {
                var st = p.statements[j];
                if (row > st.last) continue;
                place = st.note >= 0 ? 'n' + st.note : 'l' + st.line;
                what = st.what;
                from = p.rows[st.line];
                to = st.last + 1 < p.rows.length ? p.rows[st.last + 1] : p.end;
            }
        }
        var cut = kronLineAround(text, hit[0], hit[1] - hit[0], 160), marked = [];
        lines[i].marks.forEach(function(mk) {
            var a = Math.max(mk[0], cut.from), b = Math.min(mk[1], cut.to);
            if (b > a) marked.push([a - cut.from + cut.lead, b - cut.from + cut.lead]);
        });
        out.push({
            kind: p.kind, sort: p.sort, index: p.index, ref: p.ref, place: place, what: what, row: row,
            n: hitsBetween(from, hit[0]) + 1,
            text: (cut.lead ? '…' : '') + shown(cut.from, cut.to) + (cut.tail ? '…' : ''),
            marks: marked
        });
    }
    return { count: lines.length, hits: marks.length, lines: out };
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
    // The stream is built from the bytes, never read out of a Blob: in WebKit every read of a Blob inside a worker
    // fails ("NotReadableError: The I/O read operation failed."), which left the index unloaded there.
    function gunzipStream(b64) {
        var bytes = b64ToBytes(b64);
        return new ReadableStream({ start: function(c) { c.enqueue(bytes); c.close(); } })
            .pipeThrough(new DecompressionStream('gzip'));
    }
    function gunzipToText(b64) {
        return new Response(gunzipStream(b64)).text();
    }
    function gunzipToBytes(b64) {
        return new Response(gunzipStream(b64)).arrayBuffer().then(function(buf) { return new Uint8Array(buf); });
    }
    // A compressed report (plans/HTML_COMPRESSION_PLAN.md 4.4) holds every item raw, and each diagram source as a token
    // (U+0001 and its number) read from the diagram sources' pieces, which the page posts compressed with the index; the
    // decoder (self.__kronikolZstd) goes ahead of this worker's source. A piece is decoded the first time a check needs
    // one of its diagrams, and at most two are kept. A value still in gzip (H4sI), as a fragment merge took from an older
    // data file can be, is read as before; so is every value of a plain page.
    var frames = null;
    function setFrames(f) {
        frames = { pieces: f.pieces, pieceOf: [], startOf: [], endOf: [], cache: [] };
        for (var p = 0; p < f.index.length; p++) {
            var at = 0;
            for (var k = 0; k < f.index[p].length; k++) {
                frames.pieceOf.push(p); frames.startOf.push(at); at += f.index[p][k]; frames.endOf.push(at);
            }
        }
    }
    function framePiece(p) {
        for (var i = 0; i < frames.cache.length; i++) {
            if (frames.cache[i].p === p) {
                var hit = frames.cache.splice(i, 1)[0];
                frames.cache.unshift(hit);
                return hit.promise;
            }
        }
        var piece = frames.pieces[p];
        if (!piece) return Promise.reject(new Error('the diagram sources of piece ' + (p + 1) + ' are not in this file'));
        var entry = { p: p, promise: self.__kronikolZstd.decode(new Uint8Array(piece.bytes), piece.length, piece.codec) };
        frames.cache.unshift(entry);
        if (frames.cache.length > 2) frames.cache.length = 2;
        return entry.promise;
    }
    function itemText(value) {
        if (!frames || (typeof value === 'string' && value.lastIndexOf('H4sI', 0) === 0)) return gunzipToText(value);
        if (typeof value === 'string' && value.charCodeAt(0) === 1) {
            var n = Number(value.slice(1));
            return framePiece(frames.pieceOf[n]).then(function(bytes) {
                return new TextDecoder('utf-8').decode(bytes.subarray(frames.startOf[n], frames.endOf[n]));
            });
        }
        return Promise.resolve(value);
    }
    // An entry counts its text and what is kept beside it: the mask, each piece's rows, kinds and statements.
    function cachePut(i, entry) {
        entry.bytes = entry.text.length * 2 + (entry.mask ? entry.mask.length : 0);
        for (var p = 0; p < entry.pieces.length; p++) {
            var piece = entry.pieces[p];
            entry.bytes += 64 + piece.rows.byteLength + (piece.kinds ? piece.kinds.length * 32 : 0) + (piece.statements ? piece.statements.length * 48 : 0);
        }
        corpusCache.set(i, entry);
        corpusCacheBytes += entry.bytes;
        while (corpusCacheBytes > CORPUS_CACHE_LIMIT && corpusCache.size > 1) {
            var oldest = corpusCache.keys().next().value;
            corpusCacheBytes -= corpusCache.get(oldest).bytes;
            corpusCache.delete(oldest);
        }
    }
    // A scenario's text as the deep search reads it: its pieces joined with '\n' (its data-search, then its diagrams'
    // sources, its Raw PlantUML and failure texts, its flows), and what each piece is (plans/SEARCH_PLAN.md 4.6.2): where
    // it starts and ends, its kind (s the data-search, with where each of its kinds went; g a diagram's source; e the
    // failure's text; w a flow's flame text), the element it came from (its sort, its index among those, its id), where
    // each of its raw lines starts, and a diagram's statements. The letters the fold lowered are a bit each of the mask,
    // so a line is shown as written without its piece being read again.
    async function corpusForItem(i) {
        var cached = corpusCache.get(i);
        if (cached !== undefined) {
            corpusCache.delete(i); corpusCache.set(i, cached); // refresh LRU position
            return cached;
        }
        var item = items[i], parts = [], pieces = [], cases = [], at = 0, k, source;
        function add(raw, kind, sort, index, ref, bounds, statements) {
            var piece = kronCorpusPiece(raw, bounds), c;
            for (c = 0; c < piece.cases.length; c++) cases.push(at + piece.cases[c]);
            var rows = Int32Array.from(piece.rows);
            for (c = 0; c < rows.length; c++) rows[c] += at;
            var record = { kind: kind, sort: sort, index: index, ref: ref, start: at, end: at + piece.text.length, rows: rows, statements: statements };
            pieces.push(record);
            parts.push(piece.text);
            at += piece.text.length + 1;
            return record;
        }
        // the data-search, with where each kind of it starts (data-search-kinds, a letter and an offset a kind)
        var runs = (item.searchKinds || '').split(' ').filter(Boolean)
            .map(function(r) { return { kind: r.charAt(0), at: parseInt(r.slice(1), 10) || 0 }; })
            .sort(function(a, b) { return a.at - b.at; });
        if (!runs.length || runs[0].at > 0) runs.unshift({ kind: 'n', at: 0 });
        var search = add(item.searchText || '', 's', 'search', 0, null, runs.map(function(r) { return r.at; }), null);
        search.kinds = runs.map(function(r, j) { return { kind: r.kind, at: search.rows[j] }; });
        for (k = 0; k < item.diagramIds.length; k++) {
            var z = pumlMap && pumlMap[item.diagramIds[k]];
            if (!z) continue;
            source = await itemText(z);
            add(source, 'g', 'puml', k, item.diagramIds[k], null, kronDiagramStatements(source));
        }
        for (k = 0; k < item.plantumlZ.length; k++) {
            source = await itemText(item.plantumlZ[k]);
            add(source, 'g', 'z', k, (item.plantumlIds || [])[k] || null, null, kronDiagramStatements(source));
        }
        for (var raws = 0, fails = 0, r = 0; r < item.rawTexts.length; r++) {
            if ((item.rawKinds || [])[r] === 'e') add(item.rawTexts[r], 'e', 'fail', fails++, null, null, null);
            else add(item.rawTexts[r], 'g', 'raw', raws++, null, null, null);
        }
        for (k = 0; k < item.flameZ.length; k++)
            add(kronExtractFlameText(await itemText(item.flameZ[k])), 'w', 'flame', k, (item.flameIds || [])[k] || null, null, null);
        var text = parts.join('\n'), mask = null;
        if (cases.length) {
            mask = new Uint8Array((text.length + 7) >> 3);
            for (k = 0; k < cases.length; k++) mask[cases[k] >> 3] |= 1 << (cases[k] & 7);
        }
        var entry = { text: text, pieces: pieces, mask: mask };
        cachePut(i, entry);
        return entry;
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
        // each match carries the kinds of its text its positive terms hit, worked out once it matched (4.6.2): kinds[j] is
        // matches[j]'s, a string of kind letters a term of terms
        var terms = kronPositiveTerms(input, deepInput);
        var matches = [], kinds = [];
        for (var c = 0; c < candItems.length; c++) {
            if (latestGen !== gen) return; // superseded — stop wasting work
            var idx = candItems[c];
            var entry = await corpusForItem(idx), corpus = entry.text;
            var item = items[idx];
// kron:verdicts
            if (kronDeepMatchesItem(input, deepInput, corpus, new Set(item.tags), item.status, item.verdicts ? new Set(item.verdicts) : null))
// kron:else
//|            if (kronDeepMatchesItem(input, deepInput, corpus, new Set(item.tags), item.status))
// kron:/verdicts
            {
                matches.push(idx);
                kinds.push(kronItemKinds(entry, terms));
                // a batch as each hundredth match joins, never again for the candidates after it that do not match
                if (matches.length % BATCH_SIZE === 0)
                    self.postMessage({ type: 'result', gen: gen, done: false, matches: matches.slice(), terms: terms, kinds: kinds.slice() });
            }
        }
        if (latestGen !== gen) return;
        self.postMessage({
            type: 'result', gen: gen, done: true, matches: matches, terms: terms, kinds: kinds,
            stats: { candidates: candDocs.length, candidateItems: candItems.length, verified: matches.length, ms: Date.now() - t0 }
        });
    }
    // The lines of the scenarios asked about (plans/SEARCH_PLAN.md 4.6.2): snippets { gen, id, input, items, max } is
    // answered { type: 'snippets', gen, id, answers }, one answer a scenario in the order asked, { item, count, hits,
    // lines } (kronItemSnippets). A request a newer query has overtaken, before it is read or while it is, is answered
    // { type: 'snippets', gen, id, stale: true } and nothing more, as a query stops then; one that fails, with its error.
    async function runSnippets(msg) {
        var gen = msg.gen, answers = [], max = msg.max > 0 ? msg.max : 3;
        try {
            var terms = kronPositiveTerms(msg.input, kronNormalizeQueryText(msg.input));
            for (var a = 0; a < msg.items.length && latestGen === gen; a++) {
                var idx = msg.items[a];
                if (!items[idx]) { answers.push({ item: idx, count: 0, hits: 0, lines: [] }); continue; }
                var entry = await corpusForItem(idx);
                var found = kronItemSnippets(entry, terms, max);
                answers.push({ item: idx, count: found.count, hits: found.hits, lines: found.lines });
            }
        } catch (err) {
            self.postMessage({ type: 'snippets', gen: gen, id: msg.id, error: '' + err });
            return;
        }
        if (latestGen !== gen) self.postMessage({ type: 'snippets', gen: gen, id: msg.id, stale: true });
        else self.postMessage({ type: 'snippets', gen: gen, id: msg.id, answers: answers });
    }
    self.onmessage = function(e) {
        var msg = e.data;
        if (msg.type === 'init') {
            items = msg.items;
            pumlMap = msg.pumlDataJson ? JSON.parse(msg.pumlDataJson) : null;
            if (msg.frames) setFrames(msg.frames);
            gunzipToBytes(msg.indexB64).then(function(bytes) {
                ix = kronDecodeSearchIndex(bytes);
                self.postMessage({ type: 'anchors', anchors: ix.anchors, buckets: ix.buckets, docCount: ix.docCount });
            }).catch(function(err) {
                // A corrupt/undecodable blob must surface as an error, not a forever-pending
                // init: an unhandled rejection in a worker does NOT fire worker.onerror.
                self.postMessage({ type: 'fatal', message: '' + err });
            });
        } else if (msg.type === 'docmap') {
            docToItem = msg.docToItem;
            ready = true;
            var q = pendingQueries; pendingQueries = [];
            for (var i = 0; i < q.length; i++) (q[i].type === 'snippets' ? runSnippets : runQuery)(q[i]);
        } else if (msg.type === 'query') {
            latestGen = msg.gen;
            if (!ready) { pendingQueries.push(msg); return; }
            runQuery(msg);
        } else if (msg.type === 'snippets') {
            if (msg.gen !== latestGen) { self.postMessage({ type: 'snippets', gen: msg.gen, id: msg.id, stale: true }); return; }
            if (!ready) { pendingQueries.push(msg); return; }
            runSnippets(msg);
        }
    };
}

// ===== MAIN-THREAD SECTION (worker plumbing, kron-deep events, sr-flag integration) =====
(function() {
    if (typeof window === 'undefined' || typeof document === 'undefined') return; // worker/Jint context

    var state = {
        gen: 0,
        worker: null,
        initStarted: false,
        ready: false,
        shallowHidden: null,   // sr snapshot after the shallow pass, for the +N/-M chip counts
        canRemove: false,      // negated query — final deep results may hide shallow matches
        deepState: 'idle',     // what the last kron-deep event said
        posted: -1,            // the gen of the last query the worker was handed, and its input
        input: null,
        terms: [],             // its positive terms, and what they hit in each scenario it matched (item index -> kinds)
        kinds: {},
        asks: 0,               // the snippets requests the worker has not answered, by id
        pending: {}
    };

    window.__kronikolSearch = { indexState: 'unknown', docs: 0, buckets: 0, lastQuery: null, deep: 'idle' };

    function indexScript() { return document.getElementById('kron-search-index'); }

    // Where the deep search is, told as a kron-deep event on the document (plans/SEARCH_PLAN.md 4.5.7): 'working' as it
    // starts reading, 'batch' as results join, 'done' with how many it added and took away, 'idle' when it has nothing to
    // read. The Search tab's bar and counts say it, in place of the chip the search field held until the field went.
    function tell(name, detail) {
        detail = detail || {};
        detail.state = name;
        state.deepState = name;
        window.__kronikolSearch.deep = name;
        try { document.dispatchEvent(new CustomEvent('kron-deep', { detail: detail })); } catch (e) { }
    }
    function hideChip() { if (state.deepState !== 'idle') tell('idle'); }
    function showChipWorkingSoon() { tell('working'); }
    function showChipDone(added, removed) { tell('done', { added: added, removed: removed }); }

    function collectItemMeta() {
        var c = fc();
        var items = [];
        for (var i = 0; i < c.items.length; i++) {
            var el = c.items[i].el;
            var tags = Array.from(search_tags(el));
            var diagramIds = [];
            el.querySelectorAll('.plantuml-browser[id], .plantuml-inline-svg[id]').forEach(function(d) {
                // #puml-data is written once and never mutated; data-plantuml attributes are
                // rewritten on first render, so ids into the immutable map are the only safe source
                if (!d.hasAttribute('data-plantuml-z')) diagramIds.push(d.id);
            });
            // beside each piece of text, what the worker names its element by (plans/SEARCH_PLAN.md 4.6.2)
            var plantumlZ = [], plantumlIds = [];
            el.querySelectorAll('[data-plantuml-z]').forEach(function(d) { plantumlZ.push(d.getAttribute('data-plantuml-z')); plantumlIds.push(d.id || null); });
            var rawTexts = [], rawKinds = [];
            el.querySelectorAll('.raw-plantuml pre').forEach(function(p) { rawTexts.push(p.textContent); rawKinds.push('x'); });
            // failure stack traces are index-only (never data-search); the verify corpus reads
            // them back from the rendered failure pre (HTML-encoded at generation)
            el.querySelectorAll('.failure-result pre').forEach(function(p) { rawTexts.push(p.textContent); rawKinds.push('e'); });
            var flameZ = [], flameIds = [];
            el.querySelectorAll('.iflow-flame[data-flame-z]').forEach(function(f) { flameZ.push(f.getAttribute('data-flame-z')); flameIds.push(f.id || null); });
            items.push({
                searchText: c.items[i].searchText,
                searchKinds: el.getAttribute('data-search-kinds') || '',
                plantumlIds: plantumlIds,
                rawKinds: rawKinds,
                flameIds: flameIds,
                tags: tags,
                status: c.items[i].status,
// kron:verdicts
                verdicts: c.items[i].verdicts ? Array.from(c.items[i].verdicts) : null,
// kron:/verdicts
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
            kronNormalizerPasses, kronNormalizeForSearch, kronNormalizeQueryText,
            kronNormalizeRun, kronNormalizeForDisplay, kronNormalizeWithMap, kronMapBack, kronTrigramBuckets,
            kronDecodeSearchIndex, kronRowIntoBitset, kronCandidateBitsetForTerm,
            kronCandidateDocsForQuery, kronIsDeepEligible, kronDeepMatchesItem,
            kronExtractFlameText,
            kronParseNoteBlocks, kronDiagramStatements, kronCorpusPiece, kronPositiveTerms, kronItemKinds,
            kronLineAround, kronItemSnippets,
            isAdvancedSearch, advancedSearchTokenise, advancedSearchParse,
            advancedSearchEvaluate, advancedSearchMatch,
            legacySearchWords, splitLegacyTagExpression, parseSearchTokensIncludingQuotes, evaluateTagExpression,
            kronSearchWorkerMain
        ];
        var src = fns.map(function(f) { return f.toString(); }).join('\n\n') + '\n\nkronSearchWorkerMain(self);\n';
        // A compressed report's decoder goes ahead, for the diagram sources' pieces (plans/HTML_COMPRESSION_PLAN.md 4.4).
        if (window.__kronikolFrames && window.__kronikolLoader) src = window.__kronikolLoader.decoder + '\n;\n' + src;
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
            } else if (msg.type === 'snippets') {
                var ask = state.pending[msg.id];
                if (!ask) return;
                delete state.pending[msg.id];
                ask(msg.answers && msg.gen === state.gen ? withElements(msg.answers) : null);
            } else if (msg.type === 'fatal') {
                window.__kronikolSearch.indexState = 'error';
                hideChip();
                settleAsks();
            }
        };
        state.worker.onerror = function() {
            window.__kronikolSearch.indexState = 'error';
            hideChip();
            settleAsks();
        };
        state.worker.postMessage({
            type: 'init',
            indexB64: indexB64,
            pumlDataJson: pumlScript ? pumlScript.textContent : null,
            items: collectItemMeta(),
            frames: window.__kronikolFrames ? {
                pieces: window.__kronikolFrames.pieces.map(function(p) { return p ? { bytes: p.bytes, length: p.length, codec: p.codec } : null; }),
                index: window.__kronikolFrames.index
            } : null
        });
    }

    function onDeepResult(msg) {
        if (msg.gen !== state.gen) return; // stale — a newer query superseded it
        var c = fc();
        var matched = new Set(msg.matches);
        var changed = false;
        // what each match's positive terms hit (plans/SEARCH_PLAN.md 4.6.2), read through _kronDeepKinds
        state.terms = msg.terms || [];
        if (msg.kinds) for (var k = 0; k < msg.matches.length; k++) state.kinds[msg.matches[k]] = msg.kinds[k];
        if (!msg.done) {
            // progressive reveal: batches only ADD results (document order); the authoritative
            // set (which may also remove) lands with the final message
            for (var i = 0; i < c.items.length; i++) {
                if (matched.has(i) && c.items[i].sr) { c.items[i].sr = false; changed = true; }
            }
            if (changed) { applyVisibility(c); tell('batch'); }
            return;
        }
        var added = 0, removed = 0;
        for (var j = 0; j < c.items.length; j++) {
            // Authoritative (may hide a shallow match) only for negated queries; positive
            // queries are add-only — see kronQueryCanRemove.
            var newSr = state.canRemove ? !matched.has(j) : (c.items[j].sr && !matched.has(j));
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

    // A new query, or none: what the last one hit goes, and the lines asked for it are answered with nothing.
    function forget() {
        state.gen++;
        state.terms = [];
        state.kinds = {};
        settleAsks();
    }
    function settleAsks() {
        var pending = state.pending;
        state.pending = {};
        for (var id in pending) pending[id](null);
    }

    // Called by run_search_scenarios after every shallow pass (input already lowercased+trimmed).
    window._kronDeepQuery = function(input) {
        forget();
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
        state.canRemove = kronQueryCanRemove(input);
        showChipWorkingSoon();
        state.posted = state.gen;
        state.input = input;
        state.worker.postMessage({ type: 'query', gen: state.gen, input: input });
    };

    window._kronDeepReset = function() {
        forget();
        state.shallowHidden = null;
        hideChip();
    };

    function itemIndex(x) {
        var items = fc().items;
        if (typeof x === 'number') return x >= 0 && x < items.length ? x : -1;
        for (var i = 0; i < items.length; i++) if (items[i].el === x) return i;
        return -1;
    }

    // What the deep search's positive terms hit in a scenario it matched for the query in effect (plans/SEARCH_PLAN.md
    // 4.6.2): { terms, kinds }, kinds a string of kind letters a term of terms (f n d p r t e s g x the scenario's
    // data-search-kinds; g a diagram's source, e the failure's text, w the whole-test flow, which counts with the
    // diagrams), or null for a scenario it has not matched. `i` is an index into fc().items or the scenario's element.
    // They grow as the kron-deep 'batch' and 'done' events tell.
    window._kronDeepKinds = function(i) {
        var at = itemIndex(i);
        return at >= 0 && state.kinds[at] ? { terms: state.terms, kinds: state.kinds[at] } : null;
    };

    // The lines where the deep search matched, for the results on the page shown (plans/SEARCH_PLAN.md 4.6.2):
    // _kronDeepSnippets(items, max) asks for up to max lines (three when not given) of each scenario of items (indices
    // into fc().items, or the scenarios' elements) for the query in effect, and returns a promise of one answer a
    // scenario, in the order asked: { item, el, count, hits, lines }, count how many lines hold a hit and hits how many
    // hits there are, lines up to max of them in the order of the scenario's text, each
    //   { kind, sort, index, ref, el, place, what, row, n, text, marks }
    // kind g a diagram's source, e the failure's text, w the whole-test flow's flame text (the lines of the instant kinds
    // come from the page itself, 4.6.4); el the element it came from, ref its id; place where it is as hit= writes it (n3
    // a note by its original index, l14 a statement by its first line in the source, f12 a line of the flow, e the
    // failure, x a Raw PlantUML section) or null where no statement holds it, what the statement it is (note, arrow,
    // hnote, participant, partition, loop, group…; flow, error, source); row the hit's line in its piece as written, from
    // 0; n which hit of its place the line's first is, from 1; text the line as written, cut to 160 characters around its
    // first hit with "…" where it is cut; marks the [start, end] in text of each hit. The promise holds null when there
    // is nothing to ask (no index, no deep search run for the query in effect) and when a newer query overtakes it.
    window._kronDeepSnippets = function(list, max) {
        if (!state.worker || state.posted !== state.gen) return Promise.resolve(null);
        var asked = [];
        (list || []).forEach(function(x) { var at = itemIndex(x); if (at >= 0) asked.push(at); });
        var id = ++state.asks;
        return new Promise(function(resolve) {
            state.pending[id] = resolve;
            state.worker.postMessage({ type: 'snippets', gen: state.gen, id: id, input: state.input, items: asked, max: max || 3 });
        });
    };

    // the element each line came from, found as collectItemMeta read them: by its id, else by its place among its sort
    function withElements(answers) {
        var c = fc(), sorts = {
            puml: '.plantuml-browser[id], .plantuml-inline-svg[id]', z: '[data-plantuml-z]', raw: '.raw-plantuml pre',
            fail: '.failure-result pre', flame: '.iflow-flame[data-flame-z]'
        };
        answers.forEach(function(a) {
            a.el = c.items[a.item] ? c.items[a.item].el : null;
            a.lines.forEach(function(line) {
                var byId = line.ref ? document.getElementById(line.ref) : null;
                var list = byId || !a.el || !sorts[line.sort] ? [] : Array.prototype.slice.call(a.el.querySelectorAll(sorts[line.sort]));
                if (line.sort === 'puml') list = list.filter(function(d) { return !d.hasAttribute('data-plantuml-z'); });
                line.el = byId || list[line.index] || null;
            });
        });
        return answers;
    }
})();
