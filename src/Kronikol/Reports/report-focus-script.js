// Focus the keyboard moves is never left under the report bar (5.0.0, plans/OPTION_C_PLAN.md S12, D48; WCAG 2.2 2.4.11
// Focus Not Obscured and 2.4.7 Focus Visible). The scroll margins in stylesheets.css keep what the browser scrolls into
// view clear of the bar while it sticks, but a browser scrolls nothing for a control that already shows in part
// (Firefox), and no margin counts a scenario's open settings, which stick under the bar (S8). So when Tab or Shift+Tab
// moves focus, a control the pinned bar or those settings cover is brought 12 px below them, and one past the foot of the
// window 12 px above it. Left where they are: a control taller than that band which already shows in it; focus a click
// gives, which lands where the reader pointed (round 17 item 20: clicking in and out of a diagram scrolled the page), and
// focus a script gives, which lands where the script put it; and a control in the bar, in open settings stuck under it
// or in anything fixed (the internal-flow popup), which scrolling the page cannot move. On a phone the bar stays in the
// page and nothing is moved.
(function () {
    var doc = document, GAP = 12;
    function bar() { return doc.querySelector('.toolbar-row.report-nav'); }
    function sticks(nav) { var p = getComputedStyle(nav).position; return p === 'sticky' || p === 'fixed'; }

    // what scrolling the page cannot move: the bar's own controls, a scenario's open settings while they stick at their
    // line under the bar (pushed up as their diagrams leave, they move with the page), anything fixed
    function stays(t, nav) {
        if (nav.contains(t)) return true;
        var s = t.closest && t.closest('.scenario-settings.is-open');
        if (s && Math.abs(s.getBoundingClientRect().top - (parseFloat(getComputedStyle(s).top) || 0)) <= 1) return true;
        for (var n = t; n && n.nodeType === 1; n = n.parentElement) if (getComputedStyle(n).position === 'fixed') return true;
        return false;
    }

    // The top of the window's free band: below the bar while it is pinned (the mark that holds its place has gone above
    // the line it sticks at; at rest it covers nothing), and below a scenario's open settings stuck under it, for a
    // control that comes after them.
    function freeTop(t, nav) {
        var mark = doc.querySelector('.nav-mark'), line = parseFloat(getComputedStyle(nav).top) || 0;
        var top = mark && mark.getBoundingClientRect().top < line - 0.5 ? nav.getBoundingClientRect().bottom : 0;
        var open = doc.querySelectorAll('.scenario-settings.is-open');
        for (var i = 0; i < open.length; i++) {
            var s = open[i];
            if (s.contains(t) || !(s.compareDocumentPosition(t) & Node.DOCUMENT_POSITION_FOLLOWING)) continue;
            if (getComputedStyle(s).position !== 'sticky') continue;
            var r = s.getBoundingClientRect();
            if (r.height && r.top <= top + 1 && r.bottom > top) top = r.bottom;
        }
        return top + GAP;
    }

    function clear(t, nav) {
        if (doc.activeElement !== t || !sticks(nav) || stays(t, nav)) return;
        var r = t.getBoundingClientRect();
        if (!r.height) return;
        var top = freeTop(t, nav), bottom = window.innerHeight - GAP;
        // a control taller than the band that already shows in it is not hidden by the bar: bringing its top down moved a
        // whole scenario (round 17 item 20)
        if (r.height > bottom - top && r.bottom > top + 24 && r.top < bottom - 24) return;
        // a pixel's grace: the scroll margins round the bar's height (report-tabs-script.js), the band does not
        if (r.top < top - 1) window.scrollBy(0, r.top - top);
        else if (r.bottom > bottom + 1) window.scrollBy(0, Math.min(r.bottom - bottom, r.top - top));
    }

    // Focus the keyboard moved: Tab and Shift+Tab move it while the key is handled, before any timer runs, so the flag
    // is down again for a focus a script or a click gives later.
    var tabbing = false;
    window.addEventListener('keydown', function (e) {
        if (e.key !== 'Tab') return;
        tabbing = true;
        setTimeout(function () { tabbing = false; }, 0);
    }, true);

    var ENDS = ['wheel', 'touchstart', 'pointerdown', 'keydown'];
    doc.addEventListener('focusin', function (e) {
        var t = e.target, nav = bar();
        if (!tabbing || !nav || !t || t === doc.body || !t.getBoundingClientRect || !sticks(nav) || stays(t, nav)) return;
        // once the browser has scrolled it into view, if it did
        var frame = requestAnimationFrame(function () { clear(t, nav); });
        // For a second after, the page can still move under the control: a diagram above it is drawn and changes height,
        // and scroll anchoring holds what is at the top of the window, not the control. Every scroll in that second checks
        // it again, and so do timers at 150, 400 and 800 ms. The reader scrolling, pointing (a press on the scroll bar
        // too) or pressing a key ends the watch, and so does focus moving on: a key the reader presses (an arrow, Page
        // Down) moves the page their way.
        var timers = [];
        var watch = function () { clear(t, nav); };
        var stop = function () {
            cancelAnimationFrame(frame);
            window.removeEventListener('scroll', watch);
            ENDS.forEach(function (k) { window.removeEventListener(k, stop, true); });
            t.removeEventListener('focusout', stop);
            timers.forEach(clearTimeout);
        };
        window.addEventListener('scroll', watch, { passive: true });
        ENDS.forEach(function (k) { window.addEventListener(k, stop, { capture: true, passive: true }); });
        t.addEventListener('focusout', stop);
        [150, 400, 800].forEach(function (ms) { timers.push(setTimeout(watch, ms)); });
        timers.push(setTimeout(stop, 1000));
    }, true);
})();
