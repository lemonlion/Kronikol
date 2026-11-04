// node backcluster.js <TestRunReport.html> : failure cluster links to outline rows, then Back and Forward, with what shows in the row each lands on.
const path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require(path.resolve(__dirname, '../../../../tools/playwright/installed-browser.js'));
(async () => {
    const report = path.resolve(process.argv[2]);
    const browser = await launchInstalled(chromium);
    const context = await browser.newContext({ viewport: { width: 1100, height: 800 } });
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    await page.goto('file:///' + report.split(path.sep).join('/'));
    await page.waitForFunction(() => document.querySelector('details.feature') !== null, null, { polling: 200, timeout: 60000 });
    const read = () => page.evaluate(() => {
        const id = location.hash.substring(1).split('&')[0];
        let el = document.getElementById(id);
        if (!el && id.startsWith('sid-')) el = document.querySelector('[data-stable-id="' + id.slice(4) + '"]');
        const scenario = el ? el.closest('details.scenario') : null;
        if (!scenario) return { hash: location.hash.slice(0, 60), scenario: null };
        const panel = [...scenario.querySelectorAll('[id*="-detail-"]')].find(p => p.style.display !== 'none');
        return {
            hash: location.hash.slice(0, 50),
            title: scenario.querySelector(':scope > summary').textContent.trim().slice(0, 24),
            open: scenario.open,
            shown: panel ? panel.id.replace(/.*detail-/, 'row ') : '-',
            states: panel ? [...panel.querySelectorAll('details')].map(d => `${d.className.replace('scenario-', '').replace('step step-collapsible step-background', 'bgComposite').replace('step step-collapsible', 'composite')}${d.hasAttribute('data-holds-failure') ? '*' : ''}=${d.open ? 'open' : 'shut'}`).join(' ') : '',
        };
    });
    await page.evaluate(() => document.querySelectorAll('.failure-clusters, .failure-clusters details').forEach(d => d.open = true));
    const links = await page.evaluate(() => [...document.querySelectorAll('a.failure-cluster-scenario-link')].map(a => ({ href: a.getAttribute('href'), text: a.textContent.trim().slice(0, 40) })));
    const wanted = links.filter(l => /Q11/.test(l.text)).slice(0, 3);
    console.log('cluster links for Q11:', JSON.stringify(wanted.map(l => l.text)));
    for (const link of wanted) {
        await page.evaluate(() => { const tab = document.querySelector('#tab-features, [data-tab="features"]'); });
        await page.evaluate(href => { const a = [...document.querySelectorAll('a.failure-cluster-scenario-link')].find(x => x.getAttribute('href') === href); a.click(); }, link.href);
        await page.waitForTimeout(500);
        console.log('link   ', JSON.stringify(await read()));
    }
    await page.goBack(); await page.waitForTimeout(600);
    console.log('Back   ', JSON.stringify(await read()));
    await page.goBack(); await page.waitForTimeout(600);
    console.log('Back   ', JSON.stringify(await read()));
    await page.goForward(); await page.waitForTimeout(600);
    console.log('Forward', JSON.stringify(await read()));
    if (errors.length) console.log('PAGE ERRORS: ' + errors.join(' ; '));
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
