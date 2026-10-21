// Measures how a report draws long notes today. Bundled Chromium only (never an installed Chrome or Edge).
// Usage: node probe.js <report.html> [width]
const pw = require(process.env.KRONIKOL_PW || require('path').join(__dirname, 'pw', 'package'));
const { launchInstalled } = require(require('path').join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));
const path = require('path');

(async () => {
    const file = path.resolve(process.argv[2]);
    const width = parseInt(process.argv[3] || '1920', 10);
    const browser = await launchInstalled(pw.chromium);
    const page = await browser.newPage({ viewport: { width, height: 1080 } });
    const t0 = Date.now();
    await page.goto('file:///' + file.replace(/\\/g, '/'));
    await page.evaluate(() => {
        document.querySelectorAll('details').forEach(d => { d.open = true; });
        if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body);
    });
    const boxes = () => [...document.querySelectorAll('.plantuml-browser')].flatMap(c => {
        const f = c.querySelectorAll(':scope > .puml-fragment');
        return f.length ? [...f] : [c];
    });
    await page.waitForFunction(`(${boxes.toString()})().length > 0 && (${boxes.toString()})().every(e => e.querySelector('svg') || /Render|too large/.test(e.textContent || '')) && !window._plantumlRendering`,
        null, { polling: 200, timeout: 600000 });
    const drawnMs = Date.now() - t0;
    const stats = await page.evaluate(() => {
        const out = [];
        for (const c of document.querySelectorAll('.plantuml-browser')) {
            const parts = [...c.querySelectorAll(':scope > .puml-fragment')];
            const svgs = [...c.querySelectorAll('svg')];
            out.push({
                id: c.id,
                parts: parts.length,
                svgHeights: svgs.map(s => Math.round(s.getBoundingClientRect().height)),
                texts: svgs.reduce((n, s) => n + s.querySelectorAll('text').length, 0),
                elements: svgs.reduce((n, s) => n + s.querySelectorAll('*').length, 0),
                continued: (c.textContent.match(/Continued On Next Diagram/g) || []).length,
                notes: svgs.reduce((n, s) => n + (window._findNoteGroups ? window._findNoteGroups(s).length : 0), 0),
            });
        }
        return out;
    });
    // the cost of finding the notes again on what is drawn (what every redraw pays in makeNotesCollapsible)
    const findMs = await page.evaluate(() => {
        const t = performance.now();
        for (const s of document.querySelectorAll('.plantuml-browser svg')) window._findNoteGroups(s);
        return Math.round(performance.now() - t);
    });
    console.log(JSON.stringify({ file: path.basename(file), drawnMs, findNoteGroupsMs: findMs, stats }, null, 1));
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
