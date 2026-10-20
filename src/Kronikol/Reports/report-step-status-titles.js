// A step's status icon explains itself in a tooltip. The explanation used to be a title attribute on every icon, 54 to
// 209 bytes a step and repeated on every passed ancestor of a skipped or bypassed step, so a page of nested composite
// steps carried it thousands of times (plans/REPORT_FORMAT_PLAN.md section 4.8, R2). The page holds each text once,
// keyed by the icon's status class, and gives an icon its title the first time it is hovered, or its step's summary
// is focused, before the browser looks for a tooltip to show. A composite step's summary is given its own title the same
// way, saying how many steps it holds (5.0.0, plans/OPTION_C_PLAN.md D49), so the page carries no title per step there
// either.
(function () {
    var titles = __STEP_STATUS_TITLES__;

    function giveSummaryTitle(summary) {
        if (summary.hasAttribute('title')) return;
        var n = summary.parentNode.querySelectorAll(':scope > .sub-steps > .step').length;
        // Sub-steps the page holds until the step is first opened are not drawn yet: their holder says how many.
        var held = n === 0 && summary.parentNode.querySelector(':scope > .sub-steps[data-count]');
        if (held) n = parseInt(held.getAttribute('data-count'), 10) || 0;
        if (n === 0) return;
        summary.setAttribute('title', n === 1
            ? 'This step has one step inside it: click to show or hide it'
            : 'This step has ' + n + ' steps inside it: click to show or hide them');
    }

    function giveTitle(icon) {
        if (icon.hasAttribute('title')) return;
        for (var status in titles) {
            if (Object.prototype.hasOwnProperty.call(titles, status) && icon.classList.contains(status)) {
                icon.setAttribute('title', titles[status]);
                return;
            }
        }
    }

    function onPointerOrFocus(event) {
        var target = event.target;
        if (!target || target.nodeType !== 1) return;
        var summary = target.closest('details.step-collapsible > summary');
        if (summary) giveSummaryTitle(summary);
        if (target.classList.contains('step-status')) {
            giveTitle(target);
            return;
        }
        // A composite step's summary takes focus, and its icon is one of its children.
        if (event.type === 'focusin' && target.tagName === 'SUMMARY') {
            var icons = target.querySelectorAll(':scope > .step-status');
            for (var i = 0; i < icons.length; i++) giveTitle(icons[i]);
        }
    }

    document.addEventListener('mouseover', onPointerOrFocus, true);
    document.addEventListener('focusin', onPointerOrFocus, true);
})();
