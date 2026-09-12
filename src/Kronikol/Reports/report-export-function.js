function clear_all_filters() {
    var c = fc();
    // Clear search
    var sb = document.getElementById('searchbar');
    if (sb) { sb.value = ''; }
    for (var i = 0; i < c.items.length; i++) c.items[i].sr = false;
    // Clear status
    document.querySelectorAll('.status-toggle.status-active').forEach(function(b) { b.classList.remove('status-active'); });
    for (var i = 0; i < c.items.length; i++) c.items[i].st = false;
    // Clear happy paths
    var hp = document.querySelector('.happy-path-toggle.happy-path-active');
    if (hp) hp.classList.remove('happy-path-active');
    for (var i = 0; i < c.items.length; i++) c.items[i].hp = false;
    // Clear duration
    document.querySelectorAll('.percentile-btn.percentile-active').forEach(function(b) { b.classList.remove('percentile-active'); });
    var dur = document.getElementById('duration-threshold');
    if (dur) dur.value = '';
    var cw = document.getElementById('custom-duration-wrap');
    if (cw) cw.style.display = 'none';
    for (var i = 0; i < c.items.length; i++) c.items[i].dur = false;
    // Clear dependencies — the mode resets to the report's SEEDED default, not a literal
    // (a hard-coded 'AND' would silently un-configure a report generated with an OR default)
    document.querySelectorAll('.dependency-toggle.dependency-active').forEach(function(b) { b.classList.remove('dependency-active'); });
    _depMode = _depModeDefault;
    var depModeBtn = document.querySelector('.dep-mode-toggle');
    if (depModeBtn) depModeBtn.textContent = _depMode;
    for (var i = 0; i < c.items.length; i++) c.items[i].dep = false;
    // Clear categories
    document.querySelectorAll('.category-toggle.category-active').forEach(function(b) { b.classList.remove('category-active'); });
    var allCatBtn = document.querySelector('.category-toggle[data-category=""]');
    if (allCatBtn) allCatBtn.classList.add('category-active');
    if (typeof _catMode !== 'undefined') _catMode = _catModeDefault;
    var catModeBtn = document.querySelector('.cat-mode-toggle');
    if (catModeBtn) catModeBtn.textContent = _catMode;
    for (var i = 0; i < c.items.length; i++) c.items[i].cat = false;
    // Clear deep-search state (chip + in-flight queries)
    if (window._kronDeepReset) window._kronDeepReset();
    // Apply and clear URL. The anchor is not filter state: somebody who followed a link to one
    // scenario and then pressed Clear All has cleared filters, not navigated away from it.
    applyVisibility(c);
    var keptAnchor = current_url_anchor();
    history.replaceState(null, '', window.location.pathname + window.location.search + (keptAnchor ? '#' + keptAnchor : ''));
}
// Everything a diagram needs in order to draw itself is in the head — except its source. Under
// BrowserJs rendering the element is an empty `<div class="plantuml-browser" id="puml-N">`, and every
// source lives gzipped in the body's `<script id="puml-data">`, keyed by that id. Copying the head and
// the visible features alone therefore shipped the whole render machinery with none of the data:
// `getPumlZ` returned null, the render queue silently dropped the element, and every diagram that had
// not already been drawn before the export stayed blank forever — no error, no message, just an empty
// box. The body holds exactly these data payloads and no behaviour (they are `application/json`), so
// carrying them cannot re-run anything the head already ran.
function export_data_scripts(exported) {
    // `#puml-data` covers the whole report; a filtered export only needs the ids it actually contains.
    // Pruning is safe to the letter because `_pumlData` has exactly one reader — `_pumlData[el.id]` in
    // the render script's getPumlZ — so an id that is not in the exported markup can never be asked for.
    // The ids are read off the live elements rather than by re-parsing the serialised markup, which for
    // a large report is megabytes of needless work.
    var wanted = {};
    for (var i = 0; i < exported.length; i++) {
        if (exported[i].id) wanted[exported[i].id] = true;
        exported[i].querySelectorAll('[id]').forEach(function (el) { wanted[el.id] = true; });
    }

    var out = '';
    document.body.querySelectorAll(':scope > script').forEach(function (s) {
        if (s.id !== 'puml-data') { out += s.outerHTML; return; }
        var all;
        try { all = JSON.parse(s.textContent); } catch (e) { out += s.outerHTML; return; }
        var kept = {};
        for (var k in all) if (wanted[k]) kept[k] = all[k];
        // Clone the element and swap its text rather than writing the tag out by hand: the attributes
        // come from the real thing, and the report does not end up carrying a second, decoy
        // `<script id="puml-data">` in its source for anything scanning the file to trip over.
        var pruned = s.cloneNode(false);
        pruned.textContent = JSON.stringify(kept);
        out += pruned.outerHTML;
    });
    return out;
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
        featuresHtml += c.features[i].outerHTML;
    }
    var html = '<html><head>' + headHtml + '</head><body>';
    html += '<h1>Filtered Report</h1>';
    html += featuresHtml;
    html += export_data_scripts(exported);
    html += '</body></html>';
    var blob = new Blob([html], { type: 'text/html' });
    var a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'filtered-report.html';
    a.click();
    URL.revokeObjectURL(a.href);
}
function export_csv() {
    var c = fc();
    var lines = ['Feature,Scenario,Status,Duration'];
    for (var i = 0; i < c.items.length; i++) {
        var d = c.items[i];
        if (d.el.style.display === 'none') continue;
        var f = d.f;
        var fname = (f.querySelector('summary.h2') || f.querySelector('summary')).textContent.trim();
        var sname = (d.el.querySelector('summary.h3') || d.el.querySelector('summary')).textContent.trim();
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