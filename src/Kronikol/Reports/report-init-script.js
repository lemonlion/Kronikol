document.addEventListener('DOMContentLoaded', function() {
    // #10 Back-to-top FAB — show after scrolling 2 viewports
    var backToTop = document.getElementById('back-to-top');
    if (backToTop) {
        window.addEventListener('scroll', function() {
            backToTop.style.display = window.scrollY > window.innerHeight * 2 ? 'block' : 'none';
        }, { passive: true });
    }

    // The place in the address, put on the page: its tab and view, its filters, then its scenario or, on a reload, where
    // the reader was (report-navigation-script.js, which also answers an address typed into an open report, Back and
    // Forward). Last: the scroll to a `#sid-` scenario is aimed when it starts, so it has to start on the finished layout
    // (a phone's filter rows are folded by the stylesheet, from the first paint).
    if (window.kronikol_nav) window.kronikol_nav.start();
    else if (window.location.hash.length > 1) apply_url_hash(window.location.hash);
});