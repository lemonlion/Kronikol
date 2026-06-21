function update_url_hash() {
    var parts = [];
    var search = document.getElementById('searchbar');
    if (search && search.value) parts.push('q=' + encodeURIComponent(search.value));
    var statuses = [];
    document.querySelectorAll('.status-toggle.status-active').forEach(function(b) { statuses.push(b.getAttribute('data-status')); });
    if (statuses.length > 0) parts.push('status=' + statuses.join(','));
    var deps = [];
    document.querySelectorAll('.dependency-toggle.dependency-active').forEach(function(b) { deps.push(b.getAttribute('data-dependency')); });
    if (deps.length > 0) parts.push('deps=' + encodeURIComponent(deps.join(',')));
    if (_depMode !== 'AND') parts.push('depmode=' + _depMode);
    if (typeof _catMode !== 'undefined' && _catMode !== 'OR') parts.push('catmode=' + _catMode);
    if (document.querySelector('.happy-path-toggle.happy-path-active')) parts.push('hp=1');
    var cats = [];
    if (!document.querySelector('.category-toggle.category-active[data-category=""]')) {
        document.querySelectorAll('.category-toggle.category-active').forEach(function(b) { cats.push(b.getAttribute('data-category')); });
    }
    if (cats.length > 0) parts.push('cats=' + encodeURIComponent(cats.join(',')));
    var dur = document.getElementById('duration-threshold');
    if (dur && dur.value) parts.push('dur=' + dur.value);
    var activeP = document.querySelector('.percentile-btn.percentile-active');
    if (activeP) parts.push('pctl=' + encodeURIComponent(activeP.textContent));
    var hash = parts.length > 0 ? '#' + parts.join('&') : '';
    history.replaceState(null, '', window.location.pathname + window.location.search + hash);
}
function parse_url_hash() {
    var hash = window.location.hash.substring(1);
    if (!hash) return;
    // Check if it's a scenario anchor (starts with 'scenario-')
    if (hash.indexOf('scenario-') === 0) {
        var el = document.getElementById(hash);
        if (el) {
            var feature = el.closest('details.feature');
            if (feature) feature.setAttribute('open', '');
            el.setAttribute('open', '');
            el.scrollIntoView({ behavior: 'smooth', block: 'center' });
            return;
        }
        // Not a direct element — check if it's a row inside a parameterized group
        var row = document.querySelector('tr[data-scenario-id="' + hash + '"]');
        if (row) {
            var group = row.closest('details.scenario-parameterized');
            if (group) {
                var feature = group.closest('details.feature');
                if (feature) feature.setAttribute('open', '');
                group.setAttribute('open', '');
                row.click();
                group.scrollIntoView({ behavior: 'smooth', block: 'center' });
            }
        }
        return;
    }
    var params = {};
    hash.split('&').forEach(function(p) {
        var kv = p.split('=');
        if (kv.length === 2) params[kv[0]] = decodeURIComponent(kv[1]);
    });
    if (params.q) {
        var sb = document.getElementById('searchbar');
        if (sb) { sb.value = params.q; run_search_scenarios(); }
    }
    if (params.status) {
        params.status.split(',').forEach(function(s) {
            var btn = document.querySelector('.status-toggle[data-status="' + s + '"]');
            if (btn) btn.classList.add('status-active');
        });
        filter_statuses();
    }
    if (params.depmode === 'OR') {
        _depMode = 'OR';
        var modeBtn = document.querySelector('.dep-mode-toggle');
        if (modeBtn) modeBtn.textContent = 'OR';
    }
    if (params.catmode === 'AND') {
        _catMode = 'AND';
        var catModeBtn = document.querySelector('.cat-mode-toggle');
        if (catModeBtn) catModeBtn.textContent = 'AND';
    }
    if (params.deps) {
        params.deps.split(',').forEach(function(d) {
            var btn = document.querySelector('.dependency-toggle[data-dependency="' + d + '"]');
            if (btn) btn.classList.add('dependency-active');
        });
        filter_dependencies();
    }
    if (params.hp === '1') {
        var hp = document.querySelector('.happy-path-toggle');
        if (hp) { hp.classList.add('happy-path-active'); filter_happy_paths(); }
    }
    if (params.pctl) {
        document.querySelectorAll('.percentile-btn').forEach(function(b) {
            if (b.textContent === params.pctl) {
                b.classList.add('percentile-active');
                if (b.getAttribute('data-custom') === '1') {
                    var cw = document.getElementById('custom-duration-wrap');
                    if (cw) cw.style.display = 'inline-flex';
                }
            }
        });
    }
    if (params.dur) {
        var dur = document.getElementById('duration-threshold');
        if (dur) { dur.value = params.dur; filter_duration(); }
    }
    if (params.cats) {
        var allBtn = document.querySelector('.category-toggle[data-category=""]');
        if (allBtn) allBtn.classList.remove('category-active');
        params.cats.split(',').forEach(function(c) {
            var btn = document.querySelector('.category-toggle[data-category="' + c + '"]');
            if (btn) btn.classList.add('category-active');
        });
        filter_categories();
    }
}