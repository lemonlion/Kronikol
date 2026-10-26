// A Status button chosen or not, in its class and in what it says to a screen reader (aria-pressed, E2, 5.9.1: the state
// was a class alone, where the chips have said theirs since 5.0.0)
function set_status_button(btn, on) {
    btn.classList.toggle('status-active', on);
    btn.setAttribute('aria-pressed', on ? 'true' : 'false');
}
function toggle_status(btn) {
    set_status_button(btn, !btn.classList.contains('status-active'));
    filter_statuses();
}

function filter_statuses() {
    var c = fc();
    var activeSet = new Set();
    filter_box_root().querySelectorAll('.status-toggle.status-active').forEach(function(b) {
        activeSet.add(b.getAttribute('data-status'));
    });

    if (activeSet.size === 0) {
        for (var i = 0; i < c.items.length; i++) c.items[i].st = false;
        applyVisibility(c);
        update_url_hash();
        return;
    }

    // a scenario skipped after a failure is skipped, as the summary and the ring have counted it since 5.0.0 (E3, 5.9.1:
    // until then it showed under Failed)
    for (var i = 0; i < c.items.length; i++) {
        var s = c.items[i].status;
        if (s === 'SkippedAfterFailure') s = 'Skipped';
        c.items[i].st = !activeSet.has(s);
    }
    applyVisibility(c);
    update_url_hash();
}