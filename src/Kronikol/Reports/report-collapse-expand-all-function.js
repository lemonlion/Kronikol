function toggle_expand_collapse(btn, selector, expandLabel, collapseLabel) {
    var expanding = btn.textContent === expandLabel;
    var els = document.querySelectorAll(selector);
    for (var i = 0; i < els.length; i++) { if (expanding) els[i].setAttribute('open', ''); else els[i].removeAttribute('open'); }
    btn.textContent = expanding ? collapseLabel : expandLabel;
}