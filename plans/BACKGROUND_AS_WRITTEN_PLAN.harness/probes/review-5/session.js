// node session.js <rows.html>: a reader's session over gen's rows.html with a watch on every <details> in a row's panel:
// class changes by any page script, then back/forward across rows, a hit link, filters, Expand All; a class added by hand.
const path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require(path.resolve(__dirname, '../../../../tools/playwright/installed-browser.js'));

(async () => {
    const report = path.resolve(process.argv[2]);
    const url = 'file:///' + report.split(path.sep).join('/');
    const browser = await launchInstalled(chromium);
    const context = await browser.newContext({ viewport: { width: 1100, height: 800 } });
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    await page.goto(url);
    await page.waitForFunction(() => document.querySelector('details.feature') !== null, null, { polling: 200, timeout: 60000 });

    await page.evaluate(() => {
        window.__classChanges = [];
        const watch = new MutationObserver(records => records.forEach(r => window.__classChanges.push(`${r.target.className} (was ${r.oldValue}) in ${(r.target.closest('[id*="-detail-"]') || {}).id}`)));
        document.querySelectorAll('.param-detail-panel details').forEach(d => watch.observe(d, { attributes: true, attributeFilter: ['class'], attributeOldValue: true }));
        window.__shown = title => {
            const scenario = [...document.querySelectorAll('details.scenario')].find(s => s.querySelector(':scope > summary').textContent.includes(title));
            const panel = [...scenario.querySelectorAll('[id*="-detail-"]')].find(p => p.style.display !== 'none');
            return panel.id.replace(/.*detail-/, 'row ') + ': ' + [...panel.querySelectorAll('details')].map(d => `${d.className.replace('scenario-', '').replace('step step-collapsible step-background', 'bgComposite').replace('step step-collapsible', 'composite')}${d.hasAttribute('data-holds-failure') ? '*' : ''}=${d.open ? 'open' : 'shut'}`).join(' ');
        };
        window.__rowsOf = title => {
            const scenario = [...document.querySelectorAll('details.scenario')].find(s => s.querySelector(':scope > summary').textContent.includes(title));
            return [...scenario.querySelectorAll('table.param-test-table:not(.param-table-grouped) tbody tr[data-row-idx]')];
        };
    });
    const shown = t => page.evaluate(t => window.__shown(t), t);
    const log = (label, v) => console.log(label.padEnd(62), v);

    // ---- back and forward across rows, by the links a reader has ----
    const anchors = await page.evaluate(() => window.__rowsOf('Pay').map(r => r.getAttribute('data-stable-id')));
    await page.evaluate(() => { const s = [...document.querySelectorAll('details.feature')][0]; s.open = true; });
    for (const i of [1, 2, 3]) {
        await page.evaluate(a => { location.hash = '#sid-' + a; }, anchors[i]);
        await page.waitForTimeout(300);
        log(`link to row ${i} of Pay`, await shown('Pay'));
    }
    await page.goBack(); await page.waitForTimeout(400);
    log('Back', await shown('Pay'));
    await page.goBack(); await page.waitForTimeout(400);
    log('Back', await shown('Pay'));
    await page.goForward(); await page.waitForTimeout(400);
    log('Forward', await shown('Pay'));

    // ---- a reader closes the failed section on the row Back brought, goes on by link, comes back ----
    await page.evaluate(() => {
        const scenario = [...document.querySelectorAll('details.scenario')].find(s => s.querySelector(':scope > summary').textContent.includes('Pay'));
        const panel = [...scenario.querySelectorAll('[id*="-detail-"]')].find(p => p.style.display !== 'none');
        const section = panel.querySelector('details.scenario-background');
        if (section) section.querySelector(':scope > summary').click();
    });
    await page.waitForTimeout(100);
    log('reader toggles the Background section', await shown('Pay'));
    await page.goBack(); await page.waitForTimeout(400);
    log('Back', await shown('Pay'));
    await page.goForward(); await page.waitForTimeout(400);
    log('Forward', await shown('Pay'));

    // ---- a status filter, then rows, then cleared ----
    const filtered = await page.evaluate(() => {
        const b = document.querySelector('.status-toggle[data-status="Failed"], .status-toggle');
        if (!b) return 'no status toggle';
        b.click();
        return b.textContent.trim().slice(0, 30);
    });
    await page.waitForTimeout(400);
    log(`status filter clicked: ${filtered}`, await shown('Pay'));
    await page.evaluate(() => window.__rowsOf('Pay')[0].click());
    await page.waitForTimeout(100);
    log('row 0', await shown('Pay'));
    await page.evaluate(() => window.__rowsOf('Pay')[1].click());
    await page.waitForTimeout(100);
    log('row 1', await shown('Pay'));
    await page.evaluate(() => { const b = document.querySelector('.status-toggle.status-active'); if (b) b.click(); });
    await page.waitForTimeout(400);

    // ---- Expand All and Collapse All on the features and scenarios ----
    const folded = await page.evaluate(() => {
        const out = [];
        document.querySelectorAll('.fold-expand, .fold-collapse').forEach(b => out.push(b.className + ':' + (b.getAttribute('data-fold') || '')));
        return out.slice(0, 6);
    });
    log('fold controls', JSON.stringify(folded));
    await page.evaluate(() => { document.querySelectorAll('.fold-collapse').forEach(b => b.click()); });
    await page.waitForTimeout(400);
    await page.evaluate(() => { document.querySelectorAll('.fold-expand').forEach(b => b.click()); });
    await page.waitForTimeout(400);
    log('after Collapse All then Expand All', await shown('Pay'));

    // ---- what the search and a hit do to the classes ----
    await page.evaluate(() => { const bar = document.getElementById('searchbar'); bar.value = 'zebraword'; run_search_scenarios(); });
    await page.waitForTimeout(1200);
    log('filter search for a word in FirstFails row 1', await shown('FirstFails'));
    await page.evaluate(() => { const bar = document.getElementById('searchbar'); bar.value = ''; run_search_scenarios(); });
    await page.waitForTimeout(500);

    // a hit into a Background step of a chosen row, as the Search tab's line links it
    const groupId = await page.evaluate(() => {
        const scenario = [...document.querySelectorAll('details.scenario')].find(s => s.querySelector(':scope > summary').textContent.includes('FirstFails'));
        return scenario.id;
    });
    await page.evaluate(([id]) => { window.__rowsOf('FirstFails')[1].click(); location.hash = '#' + id + '&hit=' + encodeURIComponent('s:b1.1'); }, [groupId]);
    await page.waitForTimeout(1500);
    log('hit s:b1.1 with row 1 chosen', await shown('FirstFails'));
    const focus = await page.evaluate(() => { const a = document.activeElement; return a ? (a.closest('[id*="-detail-"]') || {}).id + ' :: ' + a.textContent.trim().slice(0, 60) : null; });
    log('focus went to', focus);
    const said = await page.evaluate(() => (document.querySelector('.kron-nav-status, [role="status"]') || { textContent: '' }).textContent.trim().slice(0, 100));
    log('status line', said);

    // ---- a class added by a suite's own script ----
    await page.evaluate(() => {
        const rows = window.__rowsOf('InARule');
        rows[0].click();
    });
    log('class changes the page itself made', JSON.stringify(await page.evaluate(() => window.__classChanges.slice(0, 10))));
    const added = await page.evaluate(() => {
        const rows = window.__rowsOf('Pay');
        rows[0].click();
        const scenario = rows[0].closest('details.scenario');
        const panel = () => [...scenario.querySelectorAll('[id*="-detail-"]')].find(p => p.style.display !== 'none');
        const before = panel().querySelector('details.scenario-background');
        before.open = true;
        before.classList.add('added-by-a-script');
        rows[2].click();
        const after = panel().querySelector('details.scenario-background');
        return { row0: before.className, row2Open: after.open };
    });
    log('row 0 opened with a class added, then row 2', JSON.stringify(added));
    if (errors.length) console.log('PAGE ERRORS: ' + errors.join(' ; '));
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
