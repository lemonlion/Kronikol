(function() {
    var overlay = document.createElement('div');
    overlay.className = 'lightbox-overlay';
    var img = document.createElement('img');
    overlay.appendChild(img);
    overlay.addEventListener('click', function() { overlay.classList.remove('active'); });
    window.openLightbox = function(e, anchor) {
        e.preventDefault();
        img.src = anchor.querySelector('img').src;
        img.alt = anchor.querySelector('img').alt;
        overlay.classList.add('active');
    };
    document.addEventListener('keydown', function(e) { if (e.key === 'Escape') overlay.classList.remove('active'); });
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