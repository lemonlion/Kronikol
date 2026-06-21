function toggle_examples_detail(row) {
    var detail = row.nextElementSibling;
    if (detail && detail.classList.contains('examples-detail-row')) {
        detail.style.display = detail.style.display === 'none' ? '' : 'none';
        row.classList.toggle('examples-row-expanded');
    }
}