const { run, open, chromium, launchInstalled } = require('<review-10>/lib.js');

async function status(t, name) {
    await t.openBox();
    await t.page.locator(`#filtering-box .status-toggle[data-status="${name}"]`).click();
    await t.page.waitForTimeout(200);
    await t.page.locator('#filtering-box .filters-close').click();
    return t.rec('status ' + name);
}

const scenarios = [
    { title: 'M1 same search, a status filter, Back: what the reader closed', shape: 'nested', run: async (t) => {
        await t.search('zebra'); await t.sec('bg'); await status(t, 'Passed'); await t.back();
        t.log.push(['address', await t.page.evaluate(() => location.hash)]);
        await t.forward();
        t.log.push(['address', await t.page.evaluate(() => location.hash)]);
    } },
    { title: 'M1b reader on the row with the match from the start, same search, a status filter, Back', shape: 'first', run: async (t) => {
        await t.search('kiwi'); await t.sec('bg'); await status(t, 'Passed'); await t.back();
    } },
    { title: 'M2 same search, a status filter, Back, reader moved by hand between', shape: 'nested', run: async (t) => {
        await t.search('zebra'); await t.sec('st'); await t.row('c'); await status(t, 'Passed'); await t.back(); await t.row('e');
    } },
    { title: 'M3 Clear All with a search on, Back brings it back', shape: 'nested', run: async (t) => {
        await t.search('zebra'); await t.sec('bg'); await t.clearAll(); await t.back(); await t.row('c');
    } },
];

async function measure(browser, ver, shape, width) {
    const context = await browser.newContext({ viewport: { width, height: 1000 } });
    const page = await context.newPage();
    await page.goto('file:///' + require('path').join(require('<review-10>/lib.js').ROOT, `out-${ver}/${shape}/TestRunReport.html`).replace(/\\/g, '/'));
    await page.locator('details.feature').first().waitFor({ state: 'attached' });
    for (const noun of ['features', 'scenarios']) {
        const button = page.locator(`button.fold-expand[data-fold='${noun}']`);
        if ((await button.getAttribute('aria-disabled')) !== 'true') await button.click();
    }
    await page.waitForTimeout(200);
    const out = await page.evaluate(() => {
        const rows = [];
        document.querySelectorAll('details.scenario').forEach((sc) => {
            const panel = sc.querySelector('.param-detail-panel:not([style*="display"])') || sc;
            panel.querySelectorAll('details.scenario-background, details.scenario-steps').forEach((d) => { d.open = true; });
            panel.querySelectorAll('details.step-collapsible').forEach((d) => { d.open = true; });
        });
        document.querySelectorAll('details.scenario').forEach((sc, si) => {
            const panel = sc.querySelector('.param-detail-panel:not([style*="display"])') || sc;
            panel.querySelectorAll('.step').forEach((st) => {
                const summary = st.matches('details') ? st.querySelector(':scope > summary') : null;
                const textEl = (summary || st).querySelector('.step-keyword') || (summary || st).querySelector('.step-status');
                const statusEl = (summary || st).querySelector(':scope > .step-status');
                const box = st.getBoundingClientRect();
                const cs = getComputedStyle(st);
                let triangle = null;
                if (summary) {
                    const b = getComputedStyle(summary, '::before');
                    triangle = summary.getBoundingClientRect().left + parseFloat(b.left);
                }
                const scenarioBox = (st.closest('details.scenario-steps, details.scenario-background') || st).getBoundingClientRect().left;
                rows.push({
                    sc: si,
                    where: st.parentElement.classList.contains('sub-steps') ? 'sub' : st.parentElement.classList.contains('scenario-background') ? 'section' : 'steps',
                    bg: st.classList.contains('step-background'),
                    kind: summary ? 'composite' : 'plain',
                    accent: cs.borderLeftWidth + ' ' + cs.borderLeftStyle,
                    accentAt: +(box.left - scenarioBox).toFixed(1),
                    statusAt: statusEl ? +(statusEl.getBoundingClientRect().left - scenarioBox).toFixed(1) : null,
                    triangleAt: triangle === null ? null : +(triangle - scenarioBox).toFixed(1),
                    text: (st.querySelector('.step-text') || st).textContent.trim().slice(0, 28),
                });
            });
        });
        return rows;
    });
    await context.close();
    return out;
}

(async () => {
    await run(scenarios, process.argv[2] === 'css' ? /^$/ : null);
    const browser = await launchInstalled(chromium, { headless: true });
    for (const shape of ['layout-inline', 'layout-separate']) {
        for (const ver of ['old', 'new']) {
            for (const width of [1400, 390]) {
                const rows = await measure(browser, ver, shape, width);
                console.log(`\n--- ${shape} ${ver === 'old' ? '5.28.3' : 'CUT'} ${width}px, scenario 1 (outline)`);
                for (const r of rows.filter((x) => x.sc === Number(process.env.SC || 1))) console.log(`${r.where.padEnd(8)} ${r.bg ? 'BG ' : 'own'} ${r.kind.padEnd(9)} accent ${r.accent.padEnd(12)} boxAt ${String(r.accentAt).padEnd(6)} triangleAt ${String(r.triangleAt).padEnd(6)} statusAt ${String(r.statusAt).padEnd(6)} ${r.text}`);
            }
        }
    }
    await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
