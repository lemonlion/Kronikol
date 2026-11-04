module.exports = [
    ['Move', 'A6 reader closes Steps on a, match on a, hand switch, emptied, tour', ['open', 's:scenario-steps', 'q:firstword', 1, 'q:', 2, 0]],
    ['Narrow', 'H1 several rows match, reader moves to a later match, next keystroke', ['q:sku', 2, 'q:sku1', 'q:']],
    ['Blocks', 'I1 word in the second block, emptied', ['q:blockword', 'q:', 'eval:[...document.querySelectorAll("tr.row-active")].map(r=>r.getAttribute("data-row-idx")+":"+r.closest("table").querySelectorAll("tbody tr").length).join(" ")']],
    ['Blocks', 'I2 word in the second block, emptied, reader clicks the first row, same word again', ['q:blockword', 'q:', 0, 'q:blockword', 'q:']],
    ['Blocks', 'I3 reader on a, word in the second block', ['open', 1, 'q:blockword', 'q:']],
];
