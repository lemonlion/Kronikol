var _failureIndex = -1;
function failure_scenarios() { return document.querySelectorAll('details.scenario[data-status="Failed"]'); }
// Next Failure is a place of its own (5.2.0, plans/BACK_BUTTON_NAVIGATION_PLAN.md Q3): each press adds an entry, so Back
// is the failure before it, then where the reader started.
function jump_to_next_failure() {
    var failures = failure_scenarios();
    if (failures.length === 0) return;
    var el = failures[(_failureIndex + 1) % failures.length], id = el.getAttribute('data-stable-id');
    if (window.kronikol_nav && id) window.kronikol_nav.go({ anchor: 'sid-' + id, failure: el });
    else show_failure(el);
}
function show_failure(el) {
    if (!mark_failure(el)) return;
    var feature = el.closest('details.feature');
    if (feature) feature.setAttribute('open', '');
    el.setAttribute('open', '');
    var target = el.querySelector(':scope > summary') || el;
    // Not smoothly: a smooth scroll across features the browser has never drawn is aimed at their
    // content-visibility placeholders and ends past the failure (jump_into_view says why).
    jump_into_view(target, 'start');
}
// The counter says which failure shows, after Back or Forward to one too; false for a scenario that is not a failure.
function mark_failure(el) {
    var failures = failure_scenarios(), i = Array.prototype.indexOf.call(failures, el);
    if (i < 0) return false;
    _failureIndex = i;
    var counter = document.getElementById('failure-counter');
    if (counter) counter.textContent = '(' + (i + 1) + '/' + failures.length + ')';
    return true;
}
