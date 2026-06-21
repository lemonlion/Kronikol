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
    // Clear dependencies
    document.querySelectorAll('.dependency-toggle.dependency-active').forEach(function(b) { b.classList.remove('dependency-active'); });
    _depMode = 'AND';
    var depModeBtn = document.querySelector('.dep-mode-toggle');
    if (depModeBtn) depModeBtn.textContent = 'AND';
    for (var i = 0; i < c.items.length; i++) c.items[i].dep = false;
    // Clear categories
    document.querySelectorAll('.category-toggle.category-active').forEach(function(b) { b.classList.remove('category-active'); });
    var allCatBtn = document.querySelector('.category-toggle[data-category=""]');
    if (allCatBtn) allCatBtn.classList.add('category-active');
    if (typeof _catMode !== 'undefined') _catMode = 'OR';
    var catModeBtn = document.querySelector('.cat-mode-toggle');
    if (catModeBtn) catModeBtn.textContent = 'OR';
    for (var i = 0; i < c.items.length; i++) c.items[i].cat = false;
    // Apply and clear URL
    applyVisibility(c);
    history.replaceState(null, '', window.location.pathname + window.location.search);
}
function export_html() {
    var c = fc();
    var head = document.querySelector('head');
    var headHtml = head ? head.innerHTML : '';
    var html = '<html><head>' + headHtml + '</head><body>';
    html += '<h1>Filtered Report</h1>';
    for (var i = 0; i < c.features.length; i++) {
        if (c.features[i].style.display === 'none') continue;
        html += c.features[i].outerHTML;
    }
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