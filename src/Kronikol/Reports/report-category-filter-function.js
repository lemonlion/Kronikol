var _catMode = 'OR';
function toggle_cat_mode(btn) {
    _catMode = _catMode === 'OR' ? 'AND' : 'OR';
    btn.textContent = _catMode;
    filter_categories();
}
function toggle_category(btn) {
    var cat = btn.getAttribute('data-category');
    if (cat === '') {
        // "All" button: deactivate all specific categories
        document.querySelectorAll('.category-toggle').forEach(function(b) { b.classList.remove('category-active'); });
        btn.classList.add('category-active');
    } else {
        // Deactivate "All" button, toggle this one
        var allBtn = document.querySelector('.category-toggle[data-category=""]');
        if (allBtn) allBtn.classList.remove('category-active');
        btn.classList.toggle('category-active');
        // If nothing is active, re-activate "All"
        if (document.querySelectorAll('.category-toggle.category-active').length === 0) {
            if (allBtn) allBtn.classList.add('category-active');
        }
    }
    filter_categories();
}

function filter_categories() {
    var c = fc();
    var allActive = document.querySelector('.category-toggle.category-active[data-category=""]') !== null;
    if (allActive) {
        for (var i = 0; i < c.items.length; i++) c.items[i].cat = false;
        applyVisibility(c);
        update_url_hash();
        return;
    }
    var activeSet = new Set();
    document.querySelectorAll('.category-toggle.category-active').forEach(function(b) {
        activeSet.add(b.getAttribute('data-category'));
    });
    for (var i = 0; i < c.items.length; i++) {
        var raw = c.items[i].el.getAttribute('data-categories') || '';
        var cats = raw ? new Set(raw.split(',')) : new Set();
        if (activeSet.has('__uncategorized__') && cats.size === 0) {
            c.items[i].cat = false;
        } else if (_catMode === 'AND') {
            var allMatch = true;
            activeSet.forEach(function(a) { if (a !== '__uncategorized__' && !cats.has(a)) allMatch = false; });
            c.items[i].cat = !allMatch;
        } else {
            var match = false;
            activeSet.forEach(function(a) { if (a !== '__uncategorized__' && cats.has(a)) match = true; });
            c.items[i].cat = !match;
        }
    }
    applyVisibility(c);
    update_url_hash();
}