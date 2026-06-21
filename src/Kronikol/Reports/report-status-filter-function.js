function toggle_status(btn) {
    btn.classList.toggle('status-active');
    filter_statuses();
}

function filter_statuses() {
    var c = fc();
    var activeSet = new Set();
    document.querySelectorAll('.status-toggle.status-active').forEach(function(b) {
        activeSet.add(b.getAttribute('data-status'));
    });

    if (activeSet.size === 0) {
        for (var i = 0; i < c.items.length; i++) c.items[i].st = false;
        applyVisibility(c);
        update_url_hash();
        return;
    }

    for (var i = 0; i < c.items.length; i++) {
        var s = c.items[i].status;
        if (s === 'SkippedAfterFailure') s = 'Failed';
        c.items[i].st = !activeSet.has(s);
    }
    applyVisibility(c);
    update_url_hash();
}