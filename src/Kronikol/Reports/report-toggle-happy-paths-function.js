function toggle_happy_paths(btn) {
    btn.classList.toggle('happy-path-active');
    filter_happy_paths();
}

function filter_happy_paths() {
    var c = fc();
    var active = document.querySelector('.happy-path-toggle.happy-path-active') !== null;

    for (var i = 0; i < c.items.length; i++) {
        c.items[i].hp = active && !c.items[i].isHappy;
    }
    applyVisibility(c);
    update_url_hash();
}