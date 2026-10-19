// A step's status icon explains itself in a tooltip. The explanation used to be a title attribute on every icon, 54 to
// 209 bytes a step and repeated on every passed ancestor of a skipped or bypassed step, so a page of nested composite
// steps carried it thousands of times (plans/REPORT_FORMAT_PLAN.md section 4.8, R2). The page holds each text once,
// keyed by the icon's status class, and gives an icon its title the first time it is hovered, or its step's summary
// is focused, before the browser looks for a tooltip to show.
(function () {
    var titles = __STEP_STATUS_TITLES__;

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
