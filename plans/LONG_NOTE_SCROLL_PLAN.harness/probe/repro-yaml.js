// Reproduces: Payloads -> Truncated from the toolbar judges "long" by the JSON line count while the note shows YAML.
// Usage: node repro-yaml.js <yaml-longer.html>
const pw = require(process.env.KRONIKOL_PW || require('path').join(__dirname, 'pw', 'package'));
const { launchInstalled } = require(require('path').join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));
const path = require('path');
const sleep = ms => new Promise(r => setTimeout(r, ms));

(async () => {
    const browser = await launchInstalled(pw.chromium);
    const page = await browser.newPage({ viewport: { width: 1400, height: 1000 } });
    await page.goto('file:///' + path.resolve(process.argv[2]).replace(/\\/g, '/'));
    await page.evaluate(() => {
        document.querySelectorAll('details').forEach(d => { d.open = true; });
        if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body);
    });
    const settled = () => page.waitForFunction(() => {
        const c = document.querySelector('.plantuml-browser');
        return c && c.querySelector('svg') && !window._plantumlRendering && !c._noteRendering;
    }, null, { polling: 200, timeout: 60000 });
    const rows = () => page.evaluate(() => {
        const svg = document.querySelector('.plantuml-browser svg');
        const groups = window._findNoteGroups(svg);
        const g = groups[groups.length - 1]; // the response note
        const text = g.texts.map(t => t.textContent).join('');
        return { rows: window._paintedNoteRowCount(g), endsWithEllipsis: /\.\.\.\s*$/.test(text), format: (document.querySelector('.plantuml-browser')._noteFormats || {})[1] || 'json' };
    });
    await settled(); await sleep(500);
    const atLoad = await rows();
    await page.evaluate(() => window._setReportDetails('expanded'));
    await sleep(300); await settled(); await sleep(500);
    const expanded = await rows();
    await page.evaluate(() => window._setReportDetails('truncated'));
    await sleep(300); await settled(); await sleep(500);
    const truncatedAgain = await rows();
    console.log(JSON.stringify({ atLoad, expanded, truncatedAgain }));
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
