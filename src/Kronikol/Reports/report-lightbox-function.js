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
})();