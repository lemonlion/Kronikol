// Every filter back to where the page starts, without writing the address and without closing a scenario: Clear All, and
// an address put on the page exactly (apply_url_hash).
function reset_filters() {
    var c = fc();
    // Clear search, and the qualifiers it held
    var sb = document.getElementById('searchbar');
    if (sb) { sb.value = ''; sb.classList.remove('kron-q-bad'); sb.removeAttribute('data-q-bad'); }
    for (var i = 0; i < c.items.length; i++) { c.items[i].sr = false; c.items[i].q = false; }
    // Clear status
    document.querySelectorAll('.status-toggle.status-active').forEach(function(b) { set_status_button(b, false); });
    for (var i = 0; i < c.items.length; i++) c.items[i].st = false;
    // Clear duration
    document.querySelectorAll('.percentile-btn.percentile-active').forEach(function(b) { set_percentile_button(b, false); });
    var dur = document.getElementById('duration-threshold');
    if (dur) dur.value = '';
    var cw = document.getElementById('custom-duration-wrap');
    if (cw) cw.style.display = 'none';
    for (var i = 0; i < c.items.length; i++) c.items[i].dur = false;
    // Clear the chips of tags (Happy paths with them) and dependencies. The modes reset to the report's
    // SEEDED defaults, not a literal (a hard-coded 'AND' would silently un-configure a report generated with an OR default).
    for (var family in FILTER_CHIP_ROWS) filter_chips(family).forEach(function(b) { set_chip_state(b, 'off'); });
    _tagMode = _tagModeDefault;
    set_mode_button(document.querySelector('.tag-mode-toggle'), _tagMode);
    _depMode = _depModeDefault;
    set_mode_button(document.querySelector('.dep-mode-toggle'), _depMode);
    // and each row's ways (5.10.0), which the rows' Clear keeps; sync_group_clears below shows them
    _chipWays.tag = _tagWaysDefault;
    _chipWays.dependency = _depWaysDefault;
    for (var i = 0; i < c.items.length; i++) { c.items[i].tag = false; c.items[i].dep = false; }
    sync_happy_paths();
    sync_group_clears();
    // Clear deep-search state (chip + in-flight queries)
    if (window._kronDeepReset) window._kronDeepReset();
}
function clear_all_filters() {
    reset_filters();
    applyVisibility(fc());
    // The address keeps its anchor: somebody who followed a link to one scenario and then pressed Clear All has cleared
    // filters, not navigated away from it. Clear All is a change of its own, so Back puts the filters back (5.2.0).
    update_url_hash();
}
// Everything a diagram needs in order to draw itself is in the head — except its source. Under
// BrowserJs rendering the element is an empty `<div class="plantuml-browser" id="puml-N">`, and every
// source lives gzipped in the body's `<script id="puml-data">`, keyed by that id. Copying the head and
// the visible features alone therefore shipped the whole render machinery with none of the data:
// `getPumlZ` returned null, the render queue silently dropped the element, and every diagram that had
// not already been drawn before the export stayed blank forever — no error, no message, just an empty
// box. The body holds exactly these data payloads and no behaviour (they are `application/json`), so
// carrying them cannot re-run anything the head already ran.
function export_data_scripts(exported, keptTokens) {
    // `#puml-data` covers the whole report; a filtered export only needs the ids it actually contains.
    // Pruning is safe to the letter because `_pumlData` has exactly one reader — `_pumlData[el.id]` in
    // the render script's getPumlZ — so an id that is not in the exported markup can never be asked for.
    // The ids are read off the live elements rather than by re-parsing the serialised markup, which for
    // a large report is megabytes of needless work.
    var wanted = {};
    // The sub-steps the page holds until their step is first opened (`#step-data`, 5.0.0) go the same way: only the
    // entries of holders still undrawn in the exported markup. A holder already drawn went in as the steps it drew.
    var heldSteps = {};
    for (var i = 0; i < exported.length; i++) {
        if (exported[i].id) wanted[exported[i].id] = true;
        exported[i].querySelectorAll('[id]').forEach(function (el) { wanted[el.id] = true; });
        exported[i].querySelectorAll('.sub-steps[data-steps]').forEach(function (el) { heldSteps[el.getAttribute('data-steps')] = true; });
    }

    var out = '';
    document.body.querySelectorAll(':scope > script').forEach(function (s) {
        var keep = s.id === 'puml-data' ? wanted : s.id === 'step-data' ? heldSteps : null;
        if (!keep) { out += s.outerHTML; return; }
        var all;
        try { all = JSON.parse(s.textContent); } catch (e) { out += s.outerHTML; return; }
        var kept = {};
        for (var k in all) if (keep[k]) {
            kept[k] = all[k];
            // A compressed report's diagram token (U+0001 and its number): the export carries its piece.
            if (keptTokens && typeof all[k] === 'string' && all[k].charCodeAt(0) === 1) keptTokens.push(Number(all[k].slice(1)));
        }
        // Clone the element and swap its text rather than writing the tag out by hand: the attributes
        // come from the real thing, and the report does not end up carrying a second, decoy
        // `<script id="puml-data">` in its source for anything scanning the file to trip over. A `<` is
        // written as \u003c, as the writer does: a compressed report holds its held sub-steps' HTML raw.
        var pruned = s.cloneNode(false);
        pruned.textContent = JSON.stringify(kept).replace(/</g, '\\u003c');
        out += pruned.outerHTML;
    });
    return out;
}
// A diagram the report had drawn went into the export as the SVG it drew, and nothing the render script
// had bound to it survives serialisation: its internal-flow links opened nothing and its notes no
// longer folded. A diagram queued but not yet drawn went in marked as queued, and the export's own
// pipeline, which skips a queued element, never drew it. A flame chart went in drawn and marked so, and
// no longer zoomed. The export's copy of each is put back as it was before it was drawn, so the export
// draws it again from its source and binds it as the report does. A diagram's drawn source is left out
// when the element has a source of its own (puml-data or data-plantuml-z): it was written after the
// report's filters had run, and the export applies its own. A diagram drawn when the report was written
// (NodeJs, Server, Local) cannot be drawn again, and the page marks it data-iflow-bound once it has
// bound its links; the mark means "bound in this page", so the copy goes without it.
function export_undrawn(feature) {
    var copy = feature.cloneNode(true);
    copy.querySelectorAll('.plantuml-browser').forEach(function (el) {
        if (!el.hasAttribute('data-rendered') && !el.hasAttribute('data-queued')) return;
        var ownSource = window._getPumlZ && window._getPumlZ(el);
        el.innerHTML = '';
        el.removeAttribute('data-rendered');
        el.removeAttribute('data-queued');
        if (ownSource) el.removeAttribute('data-plantuml');
    });
    copy.querySelectorAll('[data-flame-rendered]').forEach(function (el) {
        if (!el.hasAttribute('data-flame') && !el.hasAttribute('data-flame-z')) return;
        el.innerHTML = '';
        el.removeAttribute('data-flame-rendered');
    });
    copy.querySelectorAll('.plantuml-inline-svg[data-iflow-bound]').forEach(function (el) {
        el.removeAttribute('data-iflow-bound');
    });
    // Held sub-steps this page was drawing when the copy was taken: the copy draws them itself, once opened.
    copy.querySelectorAll('.sub-steps[data-drawing]').forEach(function (el) {
        el.removeAttribute('data-drawing');
    });
    // A search's marks are this page's, not the report's (plans/SEARCH_PLAN.md 4.6.5): the copy goes without them, the
    // rects in a picture it keeps as drawn and an HTML place's outline.
    copy.querySelectorAll('.kron-mark-svg').forEach(function (el) { el.parentNode.removeChild(el); });
    copy.querySelectorAll('.kron-mark-place').forEach(function (el) { el.classList.remove('kron-mark-place', 'kron-mark-arrive'); });
    // and so is the line under a diagram's view tabs saying what was turned on to show a match
    copy.querySelectorAll('.search-turned-on').forEach(function (el) { el.parentNode.removeChild(el); });
    return copy.outerHTML;
}
function export_html() {
    var c = fc();
    var head = document.querySelector('head');
    var headHtml = head ? head.innerHTML : '';
    var exported = [];
    var featuresHtml = '';
    for (var i = 0; i < c.features.length; i++) {
        if (c.features[i].style.display === 'none') continue;
        exported.push(c.features[i]);
        featuresHtml += export_undrawn(c.features[i]);
    }
    var keptTokens = [];
    var html = '<html><head>' + headHtml + '</head><body>';
    html += '<h1>Filtered Report</h1>';
    html += featuresHtml;
    html += export_data_scripts(exported, keptTokens);
    html += '</body></html>';
    if (window.__kronikolFrames && window.__kronikolLoader) {
        // From a compressed report the export is compressed too (plans/HTML_COMPRESSION_PLAN.md 4.5): the page it built,
        // gzipped by the browser's own CompressionStream into a gzip page frame, and the diagram sources' pieces its
        // diagrams name, copied as they are, around this report's loader and decoder.
        export_compressed(html, keptTokens).then(export_download, function (err) {
            if (window.console && console.error) console.error('Export failed: ' + (err && err.message ? err.message : err));
        });
        return;
    }
    export_download(html);
}
// The Search tab's exports (5.12.0): its results, whatever the filters show. The page is shown as the search leaves it
// for the copy alone, then as the filters leave it again, before it paints.
function export_search(kind) {
    var c = fc();
    show_items(c, function (d) { return d.xq || d.xs; });
    try {
        if (kind === 'csv') export_csv(); else export_html();
    } finally {
        applyVisibility(c);
    }
}
function export_download(html) {
    var blob = new Blob([html], { type: 'text/html' });
    var a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'filtered-report.html';
    a.click();
    URL.revokeObjectURL(a.href);
}
// Base64 of bytes, in slices so that no single call is given millions of arguments.
function export_base64(bytes) {
    if (typeof bytes.toBase64 === 'function') return bytes.toBase64();
    var out = '';
    for (var i = 0; i < bytes.length; i += 0x8000) out += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
    return btoa(out);
}
function export_frame(id, piece, codec, length, bytes) {
    return '<script id="' + id + '"' + (piece ? ' data-kron-piece="' + piece + '"' : '') + ' type="application/octet-stream" data-codec="'
        + codec + '" data-length="' + length + '">' + export_base64(bytes) + '<\/script>\n';
}
function export_compressed(html, tokens) {
    var frames = window.__kronikolFrames, loader = window.__kronikolLoader;
    var bytes = new TextEncoder().encode(html);
    // A browser without CompressionStream (Chrome before 80, Firefox before 113, Safari before 16.4) opens the report
    // with the page's own decoder, and stores the export's page frame as it is.
    var stored = typeof CompressionStream !== 'function';
    var framed = stored ? Promise.resolve(bytes)
        : new Response(new ReadableStream({ start: function (c) { c.enqueue(bytes); c.close(); } }).pipeThrough(new CompressionStream('gzip'))).arrayBuffer();
    return framed.then(function (gz) {
        var pieces = {};
        tokens.forEach(function (n) { var p = frames.pieceOfDiagram(n); if (p >= 0) pieces[p] = true; });
        // The hint names the file the export downloads, not the report it was made from.
        var named = function (part) { return part.replace(/kronikol decode [^<]*? -o plain\.html/g, 'kronikol decode filtered-report.html -o plain.html'); };
        var out = '<!DOCTYPE html>\n<html>\n<head>\n' + loader.head + '\n</head>\n<body>\n' + named(loader.loading) + '\n' + named(loader.notice) + '\n';
        out += export_frame('kron-frame-page', 0, stored ? 'none' : 'gzip', bytes.length, new Uint8Array(gz));
        Object.keys(pieces).map(Number).sort(function (a, b) { return a - b; }).forEach(function (p) {
            var piece = frames.pieces[p];
            out += export_frame('kron-frame-diagrams-' + (p + 1), p + 1, piece.codec, piece.length, piece.bytes);
        });
        out += '<script id="kron-diagram-index" type="application/json">' + JSON.stringify(frames.index) + '<\/script>\n';
        out += '<script id="kron-zstd">' + loader.decoder + '<\/script>\n';
        out += '<script id="kron-loader">' + loader.loader + '<\/script>\n</body>\n</html>\n';
        return out;
    });
}
// The text a summary holds directly, outside its badges, labels and buttons: the name of the feature or
// scenario it heads.
function export_summary_name(summary) {
    if (!summary) return '';
    var text = '';
    for (var n = summary.firstChild; n; n = n.nextSibling)
        if (n.nodeType === 3) text += n.nodeValue;
    return text.trim();
}
function export_csv() {
    var c = fc();
    var lines = ['Feature,Scenario,Status,Duration'];
    for (var i = 0; i < c.items.length; i++) {
        var d = c.items[i];
        // a scenario that is not a test (data-not-a-test) is no test result, as the CTRF report leaves it out
        if (d.el.style.display === 'none' || d.el.hasAttribute('data-not-a-test')) continue;
        var f = d.f;
        // A name column holds the name alone: a header also carries labels, the endpoint, the duration
        // badge, the history pill and the copy and link buttons. A scenario's copy button holds its name.
        var fname = export_summary_name(f.querySelector('summary.h2') || f.querySelector('summary'));
        var ssum = d.el.querySelector('summary.h3') || d.el.querySelector('summary');
        var copy = ssum ? ssum.querySelector('.copy-scenario-name') : null;
        var sname = copy ? copy.getAttribute('data-scenario-name') : export_summary_name(ssum);
        var dur = d.el.getAttribute('data-duration-ms') || '';
        lines.push('"' + fname.replace(/"/g,'""') + '","' + sname.replace(/"/g,'""') + '","' + d.status + '","' + dur + '"');
    }
    var blob = new Blob([lines.join('\n')], { type: 'text/csv' });
    var a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'filtered-report.csv';
    a.click();
    URL.revokeObjectURL(a.href);
}