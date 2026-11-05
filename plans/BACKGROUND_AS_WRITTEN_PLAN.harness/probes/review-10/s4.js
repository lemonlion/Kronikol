const { run } = require('<review-10>/lib.js');

async function diagramState(t, label) {
    const st = await t.page.evaluate(() => {
        const sc = document.querySelector('details.scenario-parameterized');
        const sec = sc.querySelector('details.example-diagrams');
        const shown = [...sc.querySelectorAll('[id*="-diagram-"]')].find((e) => e.style.display !== 'none');
        return 'diagramsOpen=' + (sec ? sec.open : '-') + ' shown=' + (shown ? shown.id.replace(/^.*-diagram-/, '') : '-') + ' svg=' + (shown ? !!shown.querySelector('svg') : '-');
    });
    t.log.push([label, st]);
}

const scenarios = [
    { title: 'N1 first row lacks the section: search opens it on b, ends, reader goes to b by hand, on to c', shape: 'lack', run: async (t) => {
        await t.rec('start'); await t.search('zebra'); await t.search(''); await t.row('b'); await t.row('c'); await t.row('d');
    } },
    { title: 'N1b the same, with the search still on', shape: 'lack', run: async (t) => {
        await t.search('zebra'); await t.row('c'); await t.row('b'); await t.row('d');
    } },
    { title: 'N1c first row lacks it, reader never searched: control', shape: 'lack', run: async (t) => {
        await t.row('b'); await t.row('c');
    } },
    { title: 'N2 same-row click drops what an undrawn composite would take', shape: 'held', run: async (t) => {
        await t.comp('c0'); await t.comp('c00'); await t.comp('c0'); await t.row('c'); await t.page.waitForTimeout(300);
        await t.row('c'); await t.comp('c0'); await t.page.waitForTimeout(300); await t.rec('c opened');
    } },
    { title: 'N3 the reader closes on a put-back row, then a search lands on it', shape: 'first', run: async (t) => {
        await t.search('fig'); await t.search(''); await t.sec('st'); await t.search('kiwi'); await t.row('b');
    } },
    { title: 'N4 reader on a (no mark) closes Steps, search lands on a, moves to c, empties, by hand', shape: 'first', run: async (t) => {
        await t.sec('st'); await t.search('kiwi'); await t.search('fig'); await t.search(''); await t.row('b');
    } },
    { title: 'N5 row shown matches and is not the first; typed on to another row; empties', shape: 'first', run: async (t) => {
        await t.row('c'); await t.search('fig'); await t.search('kiwi'); await t.search(''); await t.row('d');
    } },
    { title: 'M3 Clear All, Back: the address', shape: 'nested', run: async (t) => {
        await t.search('zebra'); await t.sec('bg'); await t.clearAll();
        t.log.push(['address', await t.page.evaluate(() => location.hash)]);
        await t.back();
        t.log.push(['address', await t.page.evaluate(() => location.hash + ' field=' + document.getElementById('searchbar').value)]);
    } },
    { title: 'D1 diagrams section closed: search chooses another row, draws its diagram', shape: 'diag', run: async (t) => {
        await t.rec('start'); await diagramState(t, 'before');
        await t.search('zebra'); await diagramState(t, 'after search');
        await t.page.waitForTimeout(12000); await diagramState(t, 'after 12s');
    } },
];

run(scenarios, process.argv[2] ? new RegExp(process.argv[2]) : null).catch((e) => { console.error(e); process.exit(1); });
