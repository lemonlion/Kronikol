function toggle_table_ref(btn) {
    var paramName = btn.getAttribute('data-param');
    var step = btn.closest('.step');
    var table = step ? step.querySelector('.step-param-table[data-param="' + paramName + '"]') : null;
    if (!table) {
        var scenario = btn.closest('.scenario');
        if (scenario) table = scenario.querySelector('.step-param-combined-table');
    }
    if (!table) {
        var val = btn.getAttribute('data-value');
        if (!val) return;
        var existing = btn.nextElementSibling;
        if (existing && existing.classList.contains('step-param-expand')) {
            existing.remove();
            btn.classList.remove('step-table-ref-active');
            return;
        }
        var pre = document.createElement('pre');
        pre.className = 'step-param-expand';
        pre.textContent = val;
        btn.after(pre);
        btn.classList.add('step-table-ref-active');
        return;
    }
    table.scrollIntoView({behavior:'smooth',block:'nearest'});
    var cells = table.querySelectorAll('[data-param="' + paramName + '"]');
    if (cells.length > 0) {
        for (var i = 0; i < cells.length; i++) cells[i].classList.add('step-param-highlight');
        setTimeout(function() { for (var i = 0; i < cells.length; i++) cells[i].classList.remove('step-param-highlight'); }, 1500);
    } else {
        table.classList.add('step-param-highlight');
        setTimeout(function() { table.classList.remove('step-param-highlight'); }, 1500);
    }
}