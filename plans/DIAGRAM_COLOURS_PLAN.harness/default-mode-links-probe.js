// Fourth P3 audit: what a report's internal-flow links do in the mode it was written with (the default is
// ShowLinkOnHover), on the page and in its Export Filtered HTML, measured the same way on any build.
// usage: node default-mode-links-probe.js <TestRunReport.html>
// Reads the page as a program; never prints the page. For each inline diagram (NodeJs, Server, Local), once it has
// been scrolled into view: link texts blue at rest, texts that turn blue on hover, and whether a click on the first of
// either opens a popup; then the same in the export, opened as a reader would open the file.
const path = require('path'), url = require('url'), fs = require('fs'), os = require('os');
const pw = require(path.join(path.resolve(__dirname, '..', '..'), 'tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const file = process.argv[2];

async function measure(page, label) {
    await page.evaluate(() => document.querySelectorAll('details').forEach(d => d.open = true));
    const count = await page.evaluate(() => document.querySelectorAll('.plantuml-inline-svg').length);
    // Each diagram is kept in view until the page starts binding it (the _iflowBinding expando, which an export does not
    // copy, unlike the data-iflow-bound attribute), up to 5 s: scenarios are content-visibility: auto, and a diagram near
    // the top can take several scrolls to settle into view. A hidden one (a panel, another row) is left as a reader
    // would leave it.
    let unbound = 0;
    for (let i = 0; i < count; i++) {
        const shown = await page.evaluate(k => document.querySelectorAll('.plantuml-inline-svg')[k].getClientRects().length > 0, i);
        if (!shown) continue;
        const t0 = Date.now();
        let started = false;
        while (!started && Date.now() - t0 < 5000) {
            await page.evaluate(k => document.querySelectorAll('.plantuml-inline-svg')[k].scrollIntoView({ block: 'center' }), i);
            await page.waitForTimeout(100);
            started = await page.evaluate(k => !!document.querySelectorAll('.plantuml-inline-svg')[k]._iflowBinding, i);
        }
        if (!started) unbound++;
    }
    await page.waitForTimeout(500);
    if (unbound) console.log(label + ' ' + unbound + ' shown diagram(s) never started binding');
    const facts = await page.evaluate(async () => {
        const texts = Array.from(document.querySelectorAll('.plantuml-inline-svg text'));
        const blue = t => (t.getAttribute('fill') || '').toLowerCase() === '#0000ff';
        const restBlue = texts.filter(blue);
        const hoverable = [];
        for (const t of texts) {
            if (blue(t)) continue;
            t.dispatchEvent(new MouseEvent('mouseenter', { bubbles: false, cancelable: true }));
            if (blue(t)) hoverable.push(t);
            t.dispatchEvent(new MouseEvent('mouseleave', { bubbles: false, cancelable: true }));
        }
        async function opens(t) {
            document.querySelectorAll('.iflow-overlay').forEach(o => o.remove());
            t.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
            await new Promise(r => setTimeout(r, 400));
            const opened = !!document.querySelector('.iflow-overlay .iflow-popup');
            document.querySelectorAll('.iflow-overlay').forEach(o => o.remove());
            return opened;
        }
        let hoverOpens = 0;
        for (const t of hoverable.slice(0, 20)) if (await opens(t)) hoverOpens++;
        let restOpens = 0;
        for (const t of restBlue.slice(0, 20)) if (await opens(t)) restOpens++;
        return {
            diagrams: document.querySelectorAll('.plantuml-inline-svg').length,
            anchors: document.querySelectorAll('.plantuml-inline-svg a').length,
            texts: texts.length, restBlue: restBlue.length, hoverable: hoverable.length,
            hoverOpensOfFirst20: hoverOpens, restBlueOpensOfFirst20: restOpens,
        };
    });
    console.log(label + ' ' + JSON.stringify(facts));
}

(async () => {
    const browser = await pw.chromium.launch({ headless: true });
    const context = await browser.newContext({ viewport: { width: 1600, height: 1200 }, acceptDownloads: true });
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', e => errors.push(String(e && e.message || e)));
    await page.goto(url.pathToFileURL(path.resolve(file)).href);
    await page.locator('details.feature').first().waitFor();
    await measure(page, 'PAGE');
    // Export Filtered HTML with nothing filtered: every feature goes into the export.
    const [download] = await Promise.all([
        page.waitForEvent('download'),
        page.locator('button.export-btn', { hasText: 'Export Filtered HTML' }).click(),
    ]);
    const saved = path.join(os.tmpdir(), 'p3-audit4-export-' + process.pid + '.html');
    await download.saveAs(saved);
    await page.goto(url.pathToFileURL(saved).href);
    await page.locator('details.feature').first().waitFor();
    await measure(page, 'EXPORT');
    fs.unlinkSync(saved);
    console.log('ERRORS ' + JSON.stringify(errors.slice(0, 5)));
    await browser.close();
})().catch(e => { console.log('PROBE FAILED ' + e.message); process.exit(1); });
