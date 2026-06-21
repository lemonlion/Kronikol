var _depMode = 'AND';
function toggle_dep_mode(btn) {
    _depMode = _depMode === 'AND' ? 'OR' : 'AND';
    btn.textContent = _depMode;
    filter_dependencies();
}
function toggle_dependency(btn) {
    btn.classList.toggle('dependency-active');
    filter_dependencies();
}

function filter_dependencies() {
    var c = fc();
    var activeSet = new Set();
    document.querySelectorAll('.dependency-toggle.dependency-active').forEach(function(b) {
        activeSet.add(b.getAttribute('data-dependency'));
    });

    if (activeSet.size === 0) {
        for (var i = 0; i < c.items.length; i++) c.items[i].dep = false;
        applyVisibility(c);
        update_url_hash();
        return;
    }

    var activeArr = Array.from(activeSet);
    for (var i = 0; i < c.items.length; i++) {
        var d = c.items[i];
        var match;
        if (d.deps.size === 0) {
            match = false;
        } else if (_depMode === 'AND') {
            match = true;
            for (var j = 0; j < activeArr.length; j++) {
                if (!d.deps.has(activeArr[j])) { match = false; break; }
            }
        } else {
            match = false;
            for (var j = 0; j < activeArr.length; j++) {
                if (d.deps.has(activeArr[j])) { match = true; break; }
            }
        }
        d.dep = !match;
    }
    applyVisibility(c);
    update_url_hash();
}