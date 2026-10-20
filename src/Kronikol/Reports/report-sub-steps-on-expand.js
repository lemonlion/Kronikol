// A closed composite step's sub-steps are drawn the first time it opens (5.0.0, plans/REPORT_FORMAT_PLAN.md 4.8 part b).
// The page holds their HTML gzipped in #step-data, keyed from the step's empty div.sub-steps[data-steps], which also says
// how many steps it holds (data-count). A step on the way to a failure was drawn open and is never held. A step opened by
// a click, by the keyboard or by a script (a row of a parameterized group, Expand All) fires the same toggle event.
// data-steps stays on the holder until its steps are in the page, so a holder that has it is one not drawn yet.
(function () {
    var held = null;

    function entries() {
        if (held === null) {
            var data = document.getElementById('step-data');
            held = data ? JSON.parse(data.textContent) : {};
        }
        return held;
    }

    function draw(details) {
        var holder = details.querySelector(':scope > .sub-steps[data-steps]');
        if (!holder || holder.hasAttribute('data-drawing')) return;
        var entry = entries()[holder.getAttribute('data-steps')];
        if (!entry) {
            holder.removeAttribute('data-steps');
            return;
        }
        holder.setAttribute('data-drawing', '');
        // The decompressor throws before it returns a promise where the browser has no DecompressionStream.
        new Promise(function (resolve) { resolve(decompressGzipBase64(entry)); }).then(function (html) {
            holder.insertAdjacentHTML('beforeend', html);
        }, function () {
            holder.textContent = 'These steps could not be drawn: this browser cannot decompress them.';
        }).then(function () {
            holder.removeAttribute('data-drawing');
            holder.removeAttribute('data-steps');
            // A row of a parameterized group opens the steps open in the row it replaced as they are drawn.
            holder.dispatchEvent(new CustomEvent('kron-steps-drawn', { bubbles: true }));
        });
    }

    // toggle does not bubble, so it is caught on the way down.
    document.addEventListener('toggle', function (event) {
        var target = event.target;
        if (target && target.open && target.matches && target.matches('details.step-collapsible')) draw(target);
    }, true);
})();
