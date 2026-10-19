document.addEventListener('DOMContentLoaded', function() {
    // A `#sid-` link pasted into the address bar of a report that is already open changes the hash
    // without reloading, and parse_url_hash only ever ran on load. Filters are deliberately not
    // re-applied here: update_url_hash rewrites through replaceState, which fires no hashchange, so
    // this only ever sees a fragment somebody navigated to.
    window.addEventListener('hashchange', function() {
        var anchor = current_url_anchor();
        if (anchor) reveal_url_anchor(anchor);
    });

    // #10 Back-to-top FAB — show after scrolling 2 viewports
    var backToTop = document.getElementById('back-to-top');
    if (backToTop) {
        window.addEventListener('scroll', function() {
            backToTop.style.display = window.scrollY > window.innerHeight * 2 ? 'block' : 'none';
        }, { passive: true });
    }

    // Restore filters from URL hash if present. Last, for the reason parse_url_hash reveals its anchor
    // last: the scroll to a `#sid-` scenario is aimed when it starts, so it has to start on the finished
    // layout (a phone's filter rows are folded by the stylesheet, from the first paint).
    if (window.location.hash && window.location.hash.length > 1) {
        parse_url_hash();
    }
});