const { run } = require('<review-10>/lib.js');

// opens c0 where it is closed, so the same place shows on each build
async function ensure(t, which) {
    const st = await t.state();
    if (st.includes(which + '=0')) await t.comp(which);
}

const scenarios = [
    { title: 'H0 held: hand carry works, undrawn', shape: 'held', run: async (t) => {
        await t.rec('start');
        await t.comp('c0'); await t.comp('c00'); await t.row('c'); await t.page.waitForTimeout(300); await t.rec('c settled');
        await t.comp('c0'); await t.row('d'); await t.page.waitForTimeout(300); await t.rec('d settled'); await t.comp('c0'); await t.page.waitForTimeout(300); await t.rec('d opened');
    } },
    { title: 'H1 chosen row: open held composite, leave: the nested state left on a', shape: 'held', run: async (t) => {
        await t.comp('c0'); await t.comp('c00'); await t.row('c'); await t.page.waitForTimeout(300); await t.rec('c settled');
        await t.search('zebra');
        await ensure(t, 'c0'); await t.page.waitForTimeout(300); await t.rec('b, c0 open');
        await t.row('e'); await t.page.waitForTimeout(300); await t.rec('e settled');
    } },
    { title: 'H1b chosen row: open and close held composite, leave', shape: 'held', run: async (t) => {
        await t.comp('c0'); await t.comp('c00'); await t.row('c'); await t.page.waitForTimeout(300);
        await t.comp('c0'); await t.rec('c closes c0');
        await t.search('zebra');
        await t.comp('c0'); await t.page.waitForTimeout(300); await t.comp('c0'); await t.rec('b, c0 back closed');
        await t.row('e'); await t.page.waitForTimeout(300); await t.rec('e settled');
        await t.comp('c0'); await t.page.waitForTimeout(300); await t.rec('e, c0 open');
    } },
    { title: 'H2 search moves off a chosen row with drawn-since sub-steps', shape: 'held', run: async (t) => {
        await t.comp('c0'); await t.comp('c00'); await t.row('c'); await t.page.waitForTimeout(300);
        await t.search('zebra'); await ensure(t, 'c0'); await t.page.waitForTimeout(300);
        await t.search('stripe'); await t.search(''); await t.page.waitForTimeout(300); await t.rec('put back');
        await t.row('e'); await t.page.waitForTimeout(300); await t.rec('e settled');
    } },
    { title: 'H3 put-back row with a held composite, opened after the search', shape: 'held', run: async (t) => {
        await t.comp('c0'); await t.comp('c00'); await t.comp('c0'); await t.row('c'); await t.page.waitForTimeout(300); await t.rec('c, c0 closed, undrawn');
        await t.search('zebra'); await t.search(''); await t.page.waitForTimeout(300); await t.rec('c back');
        await t.comp('c0'); await t.page.waitForTimeout(300); await t.rec('c opens c0');
        await t.row('e'); await t.page.waitForTimeout(300); await t.rec('e settled');
    } },
    { title: 'H3ctl the same with no search: control', shape: 'held', run: async (t) => {
        await t.comp('c0'); await t.comp('c00'); await t.comp('c0'); await t.row('c'); await t.page.waitForTimeout(300); await t.rec('c, c0 closed, undrawn');
        await t.comp('c0'); await t.page.waitForTimeout(300); await t.rec('c opens c0');
        await t.row('e'); await t.page.waitForTimeout(300); await t.rec('e settled');
    } },
    { title: 'H4 search lands on a held row the reader is on', shape: 'held', run: async (t) => {
        await t.row('b'); await t.search('zebra'); await t.comp('c0'); await t.page.waitForTimeout(300); await t.row('c'); await t.page.waitForTimeout(300); await t.rec('c settled');
    } },
    { title: 'B2d row shown, not the first: reader closed Steps, search lands on it, leaves', shape: 'first', run: async (t) => {
        await t.row('c'); await t.sec('st'); await t.search('fig'); await t.row('d'); await t.row('b');
    } },
    { title: 'B2e row shown is the first: reader closed Steps, search lands on it, leaves', shape: 'first', run: async (t) => {
        await t.sec('st'); await t.search('kiwi'); await t.row('b'); await t.row('d');
    } },
    { title: 'B2f row shown, closed by the reader, search lands, ends, then rows by hand', shape: 'first', run: async (t) => {
        await t.row('c'); await t.search('fig'); await t.search(''); await t.rec('after'); await t.row('d'); await t.row('a');
    } },
    { title: 'S1 toggle defaults: Background open, Steps closed: search chooses, leaves', shape: 'set-open', run: async (t) => {
        await t.rec('start'); await t.search('zebra'); await t.row('c'); await t.row('a');
    } },
    { title: 'S2 toggle defaults: reader closes Background on chosen row', shape: 'set-open', run: async (t) => {
        await t.search('zebra'); await t.sec('bg'); await t.row('c');
    } },
    { title: 'K1 Back to no search, then a second search', shape: 'nested', run: async (t) => {
        await t.search('zebra'); await t.sec('bg'); await t.back(); await t.search('zebra'); await t.row('c');
    } },
    { title: 'K2 Back and Forward', shape: 'nested', run: async (t) => {
        await t.search('zebra'); await t.sec('bg'); await t.back(); await t.forward(); await t.row('c'); await t.back(); await t.row('a'); await t.forward(); await t.search('');
    } },
    { title: 'K3 Back leaves the row: hand away, Forward chooses, then empty', shape: 'nested', run: async (t) => {
        await t.row('c'); await t.search('zebra'); await t.back(); await t.row('e'); await t.forward(); await t.search('');
    } },
    { title: 'K4 Clear All then the field emptied again', shape: 'nested', run: async (t) => {
        await t.search('zebra'); await t.clearAll(); await t.search('x'); await t.search('');
    } },
    { title: 'L1 page opened on an address with a search', shape: 'nested', hash: '#q=zebra', run: async (t) => {
        await t.rec('loaded'); await t.row('c'); await t.search(''); await t.rec('emptied');
    } },
    { title: 'L2 an address typed: a search that hid the outline, then one that lands', shape: 'nested', hash: '#q=nomatchatall', run: async (t) => {
        await t.rec('loaded');
        await t.page.evaluate(() => { location.hash = '#q=zebra'; });
        await t.page.waitForTimeout(400);
        await t.rec('q=zebra');
    } },
    { title: 'L3 search hides the group, then emptied: row and marks', shape: 'nested', run: async (t) => {
        await t.search('zebra'); await t.search('nomatchatall'); await t.search('');
    } },
    { title: 'L4 qualifier-only query ends the search: reveal afresh', shape: 'nested', run: async (t) => {
        await t.search('zebra'); await t.sec('bg'); await t.search('is:happy'); await t.search('zebra');
    } },
];

run(scenarios, process.argv[2] ? new RegExp(process.argv[2]) : null).catch((e) => { console.error(e); process.exit(1); });
