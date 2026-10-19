// A scenario's settings (5.0.0, plans/OPTION_C_PLAN.md D23, D34, R17.23 and R17.26). Its options bar sits behind a
// gear at the top right of its diagrams, which opens and closes it; on a phone the gear on the section's title does
// the same. Both gears share one state, the settings' is-open, so a bar opened at one width is open at the other.
// Nothing is remembered: each scenario's bar starts closed.
(function () {
    var CLOSED = "This scenario's payload and diagram settings, to set it apart from the report's";
    var OPEN = "Hide this scenario's payload and diagram settings";

    // A divider before the toggles only where they sit right beside the dropdowns, which happens when the bar is
    // too narrow for the toggles to keep apart at its right end. Measured, as the zones' widths depend on the words,
    // the font and the toggles a scenario has.
    function adjacent(settings) {
        var flags = settings.querySelector('.diagram-flags');
        var prev = flags && flags.previousElementSibling;
        if (!prev || !flags.offsetParent) return;
        var a = prev.getBoundingClientRect(), b = flags.getBoundingClientRect();
        var sameLine = b.top < a.bottom - 2 && b.bottom > a.top + 2;
        flags.classList.toggle('zone-adjacent', sameLine && b.left - a.right < 40);
    }

    function set(settings, open) {
        settings.classList.toggle('is-open', open);
        var gear = settings.querySelector(':scope > .scenario-settings-gear');
        if (gear) {
            gear.setAttribute('aria-expanded', open ? 'true' : 'false');
            gear.title = open ? OPEN : CLOSED;
        }
        var section = settings.parentElement;
        var titleGear = section && section.querySelector(':scope > summary > .gear-in-summary');
        if (titleGear) titleGear.setAttribute('aria-expanded', open ? 'true' : 'false');
        if (open) adjacent(settings);
    }

    document.addEventListener('click', function (e) {
        var gear = e.target.closest ? e.target.closest('.scenario-settings-gear, .gear-in-summary') : null;
        if (!gear) return;
        var settings;
        if (gear.classList.contains('gear-in-summary')) {
            // the gear sits on the section's title; a button there is its click's own target, so the section stays
            // open (Chrome and Firefox, by click, Enter and Space)
            var section = gear.closest('details');
            settings = section && section.querySelector(':scope > .scenario-settings');
        } else {
            settings = gear.parentElement;
        }
        if (settings) set(settings, !settings.classList.contains('is-open'));
    });

    var queued = false;
    window.addEventListener('resize', function () {
        if (queued) return;
        queued = true;
        requestAnimationFrame(function () {
            queued = false;
            document.querySelectorAll('.scenario-settings.is-open').forEach(adjacent);
        });
    });
})();
