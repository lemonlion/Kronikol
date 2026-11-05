const { run } = require('<review-10>/lib.js');

const scenarios = [
    { title: 'A1 hand: close Steps, past rows, reopen', shape: 'nested', run: async (t) => {
        await t.rec('start');
        await t.sec('st'); await t.row('c'); await t.row('e'); await t.sec('st'); await t.row('a');
    } },
    { title: 'A2 hand: Background open past failing and lacking rows', shape: 'fail', run: async (t) => {
        await t.rec('start');
        await t.sec('bg'); await t.row('b'); await t.row('c'); await t.row('d'); await t.row('e');
    } },
    { title: 'A3 hand: Steps closed, onto a row failing in Steps', shape: 'fail', run: async (t) => {
        await t.rec('start');
        await t.sec('st'); await t.row('d');
    } },
    { title: 'A4 hand: failed composite deep, on a row with closed composite carry', shape: 'fail', run: async (t) => {
        await t.rec('start');
        await t.row('f'); await t.row('e'); await t.row('f');
    } },
    { title: 'A5 hand: nested composite carried, undrawn', shape: 'nested', run: async (t) => {
        await t.rec('start');
        await t.comp('c0'); await t.comp('c00'); await t.row('b'); await t.row('c'); await t.comp('c0'); await t.row('d'); await t.comp('c0');
    } },
    { title: 'B1 search chooses: takes nothing, opened not carried', shape: 'nested', run: async (t) => {
        await t.row('c'); await t.sec('st');
        await t.search('zebra'); await t.row('e'); await t.row('b'); await t.search('');
    } },
    { title: 'B2 search lands on the row shown (first row)', shape: 'first', run: async (t) => {
        await t.rec('start');
        await t.search('kiwi'); await t.row('b'); await t.row('d');
    } },
    { title: 'B2b search lands on the row shown (not the first row)', shape: 'first', run: async (t) => {
        await t.row('c');
        await t.search('fig'); await t.row('d'); await t.search(''); await t.row('b');
    } },
    { title: 'B2c search names the outline, reader on row b, no row text holds it', shape: 'name', run: async (t) => {
        await t.row('b');
        await t.search('walrus'); await t.row('c'); await t.search(''); await t.row('a');
    } },
    { title: 'B3 several match, reader opens on the row chosen, then narrows', shape: 'two', run: async (t) => {
        await t.search('heron'); await t.sec('bg'); await t.search('"a heron customer"'); await t.row('d');
    } },
    { title: 'B4 second search on a put-back row the reader opened', shape: 'first', run: async (t) => {
        await t.row('b');
        await t.search('fig'); await t.search(''); await t.sec('bg'); await t.search('customer'); await t.note('customer in a and c, several rows; narrowing below');
    } },
    { title: 'B4b reader opens on a put-back row, then a search lands on it', shape: 'first', run: async (t) => {
        await t.search('fig'); await t.search(''); await t.sec('bg'); await t.search('kiwi'); await t.row('b');
    } },
    { title: 'B5 chosen row: open a composite, nested one drawn closed, leave', shape: 'nested', run: async (t) => {
        await t.comp('c0'); await t.comp('c00'); await t.row('c');
        await t.search('zebra'); await t.comp('c0'); await t.row('e'); await t.search('');
    } },
    { title: 'B5b put-back row: open a composite, nested one drawn closed, leave', shape: 'nested', run: async (t) => {
        await t.comp('c0'); await t.comp('c00'); await t.row('c'); await t.comp('c0');
        await t.search('zebra'); await t.search(''); await t.comp('c0'); await t.row('e');
    } },
    { title: 'B6 chosen row: close and reopen what the search opened', shape: 'nested', run: async (t) => {
        await t.search('zebra'); await t.sec('bg'); await t.sec('bg'); await t.row('c');
    } },
    { title: 'B6b chosen row: reader had Steps closed, closes and reopens Steps there', shape: 'nested', run: async (t) => {
        await t.sec('st'); await t.search('zebra'); await t.sec('st'); await t.sec('st'); await t.row('c');
    } },
    { title: 'B7 Clear All: row, marks, second search, stale row', shape: 'nested', run: async (t) => {
        await t.search('zebra'); await t.clearAll(); await t.row('c'); await t.search('stripe'); await t.search('');
    } },
    { title: 'B8 Back and Forward', shape: 'nested', run: async (t) => {
        await t.search('zebra'); await t.sec('bg'); await t.search('stripe'); await t.back(); await t.back(); await t.forward(); await t.forward();
    } },
    { title: 'B9 two outlines: one search chooses in each, empties', shape: 'two', run: async (t) => {
        await t.sec('st'); await t.search('heron'); await t.search(''); t.s = 1; await t.rec('refund'); await t.row('c'); t.s = 0; await t.rec('pay');
    } },
    { title: 'B9b two outlines: reader state stays per outline through a search', shape: 'two', run: async (t) => {
        await t.sec('bg'); await t.row('d'); t.s = 1; await t.rec('refund'); await t.row('c');
        t.s = 0; await t.search('heron'); await t.rec('pay'); t.s = 1; await t.rec('refund'); await t.row('a');
    } },
    { title: 'B10 the search chooses again a row the reader has been to', shape: 'nested', run: async (t) => {
        await t.search('zebra'); await t.sec('bg'); await t.row('c'); await t.row('b'); await t.row('e');
        await t.search('stripe'); await t.search('zebra'); await t.row('c');
    } },
    { title: 'B10b chooses again after the search ended, reader closed there', shape: 'nested', run: async (t) => {
        await t.search('zebra'); await t.sec('bg'); await t.row('c'); await t.search(''); await t.search('zebra'); await t.row('e');
    } },
    { title: 'B10c reader came back by hand, then a new search lands on that row', shape: 'nested', run: async (t) => {
        await t.search('zebra'); await t.row('c'); await t.row('b'); await t.clearAll(); await t.search('zebra'); await t.row('e');
    } },
    { title: 'B11 a mark on a put-back row, then the reader changes a thing', shape: 'nested', run: async (t) => {
        await t.search('zebra'); await t.search(''); await t.sec('st'); await t.row('c'); await t.row('a');
    } },
    { title: 'B12 reader closes on the chosen row, empties, moves', shape: 'nested', run: async (t) => {
        await t.search('zebra'); await t.sec('st'); await t.search(''); await t.row('c'); await t.row('a');
    } },
    { title: 'B13 failing row chosen, reader closes, comes back', shape: 'fail', run: async (t) => {
        await t.search('lynx'); await t.row('b'); await t.sec('bg'); await t.row('g'); await t.row('b');
    } },
    { title: 'B14 search onto a never-started row, reader closed Failure Result earlier', shape: 'fail', run: async (t) => {
        await t.row('c'); await t.sec('fr'); await t.search('okapi'); await t.row('c');
    } },
    { title: 'B15 search opened row: Steps closed by reader moved on, search moves back', shape: 'nested', run: async (t) => {
        await t.search('zebra'); await t.sec('st'); await t.search('stripe'); await t.search('zebra'); await t.row('c');
    } },
];

run(scenarios, process.argv[2] ? new RegExp(process.argv[2]) : null).catch((e) => { console.error(e); process.exit(1); });
