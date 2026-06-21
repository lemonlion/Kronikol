document.addEventListener('DOMContentLoaded', function() {
    // Restore filters from URL hash if present
    if (window.location.hash && window.location.hash.length > 1) {
        parse_url_hash();
    }

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

    // #4 Per-scenario diagram controls toggle on mobile
    if (isMobile) {
        document.querySelectorAll('.diagram-toggle').forEach(function(controls) {
            controls.classList.add('scenario-diagram-controls');
            controls.style.display = 'none';
            var btn = document.createElement('button');
            btn.className = 'scenario-diagram-controls-toggle';
            btn.textContent = '\u2699 Diagram Settings';
            btn.style.display = 'block';
            controls.parentNode.insertBefore(btn, controls);
            btn.addEventListener('click', function() {
                var showing = controls.style.display === 'flex';
                controls.style.display = showing ? 'none' : 'flex';
                btn.textContent = showing ? '\u2699 Diagram Settings' : '\u2699 Hide Settings';
            });
        });
    }
});