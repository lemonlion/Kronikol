document.addEventListener('keydown', function(e) {
    // a field keeps its keys: until 5.0.0 a dropdown's arrow keys moved between scenarios instead of changing its value
    if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA' || e.target.tagName === 'SELECT') return;
    // nor does a tab list: Up and Down on a report tab moved a scenario inside the hidden Features panel (5.2.0, S11)
    if (e.target.closest && e.target.closest('[role="tablist"]')) return;
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
    } else if (e.key === 'Enter' && focused && (e.target === document.body || e.target === document.documentElement)) {
        // only while nothing has the keyboard's focus: Enter on a button presses it (until 5.0.0 it folded the
        // scenario the arrow keys had picked, and the button did nothing)
        e.preventDefault();
        if (focused.hasAttribute('open')) focused.removeAttribute('open');
        else focused.setAttribute('open', '');
    }
});