var _failureIndex = -1;
function jump_to_next_failure() {
    var failures = document.querySelectorAll('details.scenario[data-status="Failed"]');
    if (failures.length === 0) return;
    _failureIndex = (_failureIndex + 1) % failures.length;
    var el = failures[_failureIndex];
    var feature = el.closest('details.feature');
    if (feature) feature.setAttribute('open', '');
    el.setAttribute('open', '');
    var target = el.querySelector(':scope > summary') || el;
    target.scrollIntoView({ behavior: 'smooth', block: 'start' });
    var counter = document.getElementById('failure-counter');
    if (counter) counter.textContent = '(' + (_failureIndex + 1) + '/' + failures.length + ')';
}