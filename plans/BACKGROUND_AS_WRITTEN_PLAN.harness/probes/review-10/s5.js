// The one-line fixes the review suggests, put on this cut's page from outside (nothing under the repo is written): do the faults go, and does what the suite pins stay?
const { run } = require('<review-10>/lib.js');

const FIX = () => {
    // F1, F2: before the search opens a closed section, what the reader has there so far is kept, and the mark is taken afresh; a section already open is the reader's
    selectRowOpenedBySearch = function (panel, section) {
        if (section.open || !panel.classList.contains('param-detail-panel') || !panel.contains(section)) return;
        const prefix = panel.id.replace(/-detail-\d+$/, '');
        const kept = selectRowKept[prefix] || (selectRowKept[prefix] = { sections: Object.create(null), steps: Object.create(null) });
        selectRowKeep(kept, selectRowStates(panel), selectRowChosen.get(panel));
        const mark = selectRowStates(panel);
        mark.sections[section.classList[0]] = true;
        selectRowChosen.set(panel, mark);
    };
    revealMatchSections = function (el) {
        const shown = el.querySelector('.param-detail-panel:not([style*="display"])') || el;
        [el.querySelector('details.example-diagrams'), shown.querySelector('details.scenario-steps'), shown.querySelector('details.scenario-background')].forEach(function (section) {
            if (section && !_sectionsRevealed.has(section)) {
                _sectionsRevealed.add(section);
                selectRowOpenedBySearch(shown, section);
                section.setAttribute('open', '');
            }
        });
    };
    // F4: a step drawn since the mark starts closed, so closed is no change
    selectRowKeep = function (kept, left, chosen) {
        ['sections', 'steps'].forEach(function (kind) {
            for (const key in left[kind])
                if (!chosen || !!chosen[kind][key] !== left[kind][key]) kept[kind][key] = left[kind][key];
        });
    };
    // F3: an address holding the search already on ends nothing
    const forget = forget_sections_revealed;
    let hold = false;
    forget_sections_revealed = function () { if (!hold) forget(); };
    const apply = apply_url_hash;
    apply_url_hash = function (hash) {
        const q = new URLSearchParams(String(hash || '').replace(/^#/, '')).get('q');
        hold = !!q && q === document.getElementById('searchbar').value;
        try { apply(hash); } finally { hold = false; }
    };
};

async function fix(t) { if (t.ver === 'new') await t.page.evaluate(FIX); }
async function status(t, name) {
    await t.openBox();
    await t.page.locator(`#filtering-box .status-toggle[data-status="${name}"]`).click();
    await t.page.waitForTimeout(200);
    await t.page.locator('#filtering-box .filters-close').click();
    return t.rec('status ' + name);
}
async function ensure(t, which) {
    const st = await t.state();
    if (st.includes(which + '=0')) await t.comp(which);
}

const scenarios = [
    { title: 'F1 row shown, not the first', shape: 'first', run: async (t) => { await fix(t); await t.row('c'); await t.sec('st'); await t.search('fig'); await t.row('d'); await t.row('b'); } },
    { title: 'F1b row shown is the first', shape: 'first', run: async (t) => { await fix(t); await t.sec('st'); await t.search('kiwi'); await t.row('b'); await t.row('d'); } },
    { title: 'F2 several match, reader opens, narrows', shape: 'two', run: async (t) => { await fix(t); await t.search('heron'); await t.sec('bg'); await t.search('"a heron customer"'); await t.row('d'); } },
    { title: 'F2b put-back row opened, search lands', shape: 'first', run: async (t) => { await fix(t); await t.search('fig'); await t.search(''); await t.sec('bg'); await t.search('kiwi'); await t.row('b'); } },
    { title: 'N3 closed on put-back row, search lands', shape: 'first', run: async (t) => { await fix(t); await t.search('fig'); await t.search(''); await t.sec('st'); await t.search('kiwi'); await t.row('b'); } },
    { title: 'F3 Back to the same search', shape: 'nested', run: async (t) => { await fix(t); await t.search('zebra'); await t.sec('bg'); await status(t, 'Passed'); await t.back(); await t.forward(); } },
    { title: 'F3b Back after a hand move', shape: 'nested', run: async (t) => { await fix(t); await t.search('zebra'); await t.sec('st'); await t.row('c'); await status(t, 'Passed'); await t.back(); await t.row('e'); } },
    { title: 'F4 chosen row opens a held composite', shape: 'held', run: async (t) => { await fix(t); await t.comp('c0'); await t.comp('c00'); await t.row('c'); await t.page.waitForTimeout(300); await t.search('zebra'); await ensure(t, 'c0'); await t.page.waitForTimeout(300); await t.row('e'); await t.page.waitForTimeout(300); await t.rec('e settled'); } },
    { title: 'F4b open and close', shape: 'held', run: async (t) => { await fix(t); await t.comp('c0'); await t.comp('c00'); await t.row('c'); await t.page.waitForTimeout(300); await t.comp('c0'); await t.search('zebra'); await t.comp('c0'); await t.page.waitForTimeout(300); await t.comp('c0'); await t.row('e'); await t.page.waitForTimeout(300); await t.comp('c0'); await t.page.waitForTimeout(300); await t.rec('e, c0 open'); } },
    { title: 'P1 reader opens sub-step on chosen row: carries', shape: 'held', run: async (t) => { await fix(t); await t.search('zebra'); await t.comp('c0'); await t.page.waitForTimeout(300); await t.comp('c00'); await t.row('e'); await t.page.waitForTimeout(300); await t.rec('e settled'); } },
    { title: 'P2 pinned: chosen row, closed by reader on the row left', shape: 'nested', run: async (t) => { await fix(t); await t.sec('st'); await t.search('zebra'); await t.row('c'); await t.row('a'); } },
    { title: 'P3 pinned: reader changes on chosen row', shape: 'nested', run: async (t) => { await fix(t); await t.search('zebra'); await t.sec('st'); await t.row('c'); } },
    { title: 'P4 pinned: search opened does not carry', shape: 'nested', run: async (t) => { await fix(t); await t.search('zebra'); await t.row('c'); } },
    { title: 'P5 pinned: come back by hand is theirs', shape: 'nested', run: async (t) => { await fix(t); await t.sec('st'); await t.search('zebra'); await t.row('c'); await t.row('b'); await t.sec('st'); await t.row('c'); } },
    { title: 'P6 pinned: moves away and back stays closed', shape: 'nested', run: async (t) => { await fix(t); await t.search('zebra'); await t.sec('bg'); await t.search('stripe'); await t.search('zebra'); } },
    { title: 'P7 pinned: new search reveals afresh; Back and Clear All too', shape: 'nested', run: async (t) => { await fix(t); await t.search('zebra'); await t.sec('bg'); await t.search(''); await t.search('zebra'); await t.sec('bg'); await t.clearAll(); await t.search('zebra'); await t.sec('bg'); await t.back(); await t.search('zebra'); } },
    { title: 'P8 pinned: empties puts back', shape: 'nested', run: async (t) => { await fix(t); await t.row('c'); await t.sec('st'); await t.search('zebra'); await t.search(''); } },
];

run(scenarios, process.argv[2] ? new RegExp(process.argv[2]) : null).catch((e) => { console.error(e); process.exit(1); });
