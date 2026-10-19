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

    // #2 Collapsible filter section on mobile
    var isMobile = window.matchMedia('(max-width: 768px)').matches;
    if (isMobile) {
        var filtersDiv = document.querySelector('.filters');
        var filterToggle = document.querySelector('.mobile-filter-toggle');
        if (filtersDiv && filterToggle) {
            filtersDiv.style.display = 'none';
            filterToggle.addEventListener('click', function() {
                var isOpen = filterToggle.classList.toggle('filter-open');
                filtersDiv.style.display = isOpen ? 'flex' : 'none';
            });
        }
    }

    // Restore filters from URL hash if present. Last, for the reason parse_url_hash reveals its anchor
    // last: the scroll to a `#sid-` scenario is aimed when it starts, so it has to start on the finished
    // layout. Run first, it was aimed before the mobile blocks above folded the filter panel away, and
    // ended the panel's height too far down, with the scenario off the top of a phone's screen.
    if (window.location.hash && window.location.hash.length > 1) {
        parse_url_hash();
    }
});