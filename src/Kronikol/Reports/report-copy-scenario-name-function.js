function copy_scenario_name(btn, evt) {
    evt.stopPropagation();
    evt.preventDefault();
    var name = btn.getAttribute('data-scenario-name');
    navigator.clipboard.writeText(name).then(function() {
        var orig = btn.textContent;
        btn.textContent = '\u2713';
        setTimeout(function() { btn.textContent = orig; }, 1500);
    });
}