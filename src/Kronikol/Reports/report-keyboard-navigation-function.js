document.addEventListener('keydown', function(e) {
    if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') return;
    if (e.key === '/') {
        e.preventDefault();
        var sb = document.getElementById('searchbar');
        if (sb) sb.focus();
        return;
    }
    var scenarios = Array.from(document.querySelectorAll('details.scenario')).filter(function(s) { return s.style.display !== 'none'; });
    if (scenarios.length === 0) return;
    var focused = document.querySelector('.scenario-focused');
    var idx = focused ? scenarios.indexOf(focused) : -1;
    if (e.key === 'ArrowDown') {
        e.preventDefault();
        if (focused) focused.classList.remove('scenario-focused');
        idx = (idx + 1) % scenarios.length;
        scenarios[idx].classList.add('scenario-focused');
        scenarios[idx].scrollIntoView({ behavior: 'smooth', block: 'center' });
        var feature = scenarios[idx].closest('details.feature');
        if (feature) feature.setAttribute('open', '');
    } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        if (focused) focused.classList.remove('scenario-focused');
        idx = idx <= 0 ? scenarios.length - 1 : idx - 1;
        scenarios[idx].classList.add('scenario-focused');
        scenarios[idx].scrollIntoView({ behavior: 'smooth', block: 'center' });
        var feature = scenarios[idx].closest('details.feature');
        if (feature) feature.setAttribute('open', '');
    } else if (e.key === 'Enter' && focused) {
        e.preventDefault();
        if (focused.hasAttribute('open')) focused.removeAttribute('open');
        else focused.setAttribute('open', '');
    }
});