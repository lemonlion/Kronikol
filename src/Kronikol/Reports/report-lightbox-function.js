(function() {
    var overlay = document.createElement('div');
    overlay.className = 'lightbox-overlay';
    var img = document.createElement('img');
    overlay.appendChild(img);
    function isOpen() { return overlay.classList.contains('active'); }
    function show(anchor) {
        var shown = anchor && anchor.querySelector('img');
        if (!shown) return false;
        img.src = shown.src;
        img.alt = shown.alt;
        overlay.classList.add('active');
        return true;
    }
    // The lightbox is a layer of the session history (5.2.0, plans/BACK_BUTTON_NAVIGATION_PLAN.md 4.7): opening it adds
    // an entry, so Back (or a phone's swipe back) closes it rather than leaving the report, and Forward opens it again.
    // Closed by a click or Esc, it takes its entry back out, and the focus returns to the image's link.
    window.kronikol_layers = window.kronikol_layers || {};
    window.kronikol_layers.lightbox = {
        isOpen: isOpen,
        open: function(data) { if (window.kronikol_nav) show(window.kronikol_nav.find(data && data.at)); },
        close: function() { overlay.classList.remove('active'); }
    };
    function close() {
        if (!isOpen()) return;
        overlay.classList.remove('active');
        if (window.kronikol_nav) window.kronikol_nav.layer('lightbox', null, false);
    }
    overlay.addEventListener('click', close);
    window.openLightbox = function(e, anchor) {
        e.preventDefault();
        if (!show(anchor)) return;
        if (window.kronikol_nav) window.kronikol_nav.layer('lightbox', { at: window.kronikol_nav.address(anchor) }, true, anchor);
    };
    document.addEventListener('keydown', function(e) { if (e.key === 'Escape') close(); });
    document.addEventListener('DOMContentLoaded', function() { document.body.appendChild(overlay); });
    // An attachment image that does not load (#148): a report folder copied without its attachments/, or an exported copy
    // saved elsewhere. Drawn as its name and why, as one the report does not hold is. One capture-phase listener on the
    // document, registered in the head before any image loads, so an Export Filtered HTML copy, which keeps this script,
    // does the same.
    document.addEventListener('error', function(e) {
        var image = e.target;
        if (!image || image.tagName !== 'IMG' || !image.classList.contains('attachment-image')) return;
        var drawn = image.closest('a.attachment-image-link') || image;
        if (!drawn.parentNode) return;
        var missing = document.createElement('span');
        missing.className = 'attachment-missing';
        missing.title = image.getAttribute('src') || '';
        missing.textContent = (image.alt || '') + ' (could not be loaded)';
        drawn.parentNode.replaceChild(missing, drawn);
    }, true);
})();