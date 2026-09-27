// Third P3 audit: what a NodeJs report does with its internal-flow links and its embedded component diagram.
// usage: node nodejs-links-probe.js <TestRunReport.html>
// Reads the page as a program; never prints the page.
const path = require('path'), url = require('url');
const pw = require(path.join(path.resolve(__dirname, '..', '..'), 'tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const file = process.argv[2];

(async () => {
    const browser = await pw.chromium.launch({ headless: true });
    const page = await (await browser.newContext({ viewport: { width: 1600, height: 1200 } })).newPage();
    const errors = [];
    page.on('pageerror', e => errors.push(String(e && e.message || e)));
    await page.goto(url.pathToFileURL(path.resolve(file)).href);
    await page.locator('details.feature').first().waitFor();
    await page.evaluate(() => document.querySelectorAll('details').forEach(d => d.open = true));
    await page.waitForTimeout(1500);
    const facts = await page.evaluate(() => {
        const inline = Array.from(document.querySelectorAll('.plantuml-inline-svg'));
        const browserDivs = Array.from(document.querySelectorAll('.plantuml-browser'));
        const out = {
            inlineContainers: inline.length,
            inlineWithSvg: inline.filter(d => d.querySelector('svg')).length,
            anchors: inline.reduce((n, d) => n + d.querySelectorAll('a[href^="#iflow-"], a[*|href^="#iflow-"]').length, 0),
            blueTexts: inline.reduce((n, d) => n + Array.from(d.querySelectorAll('text')).filter(t => (t.getAttribute('fill') || '').toLowerCase() === '#0000ff').length, 0),
            browserDivs: browserDivs.length,
            browserDivsWithSvg: browserDivs.filter(d => d.querySelector('svg')).length,
            componentPanel: !!document.getElementById('component-diagram'),
            componentPanelSvg: !!document.querySelector('#component-diagram svg'),
            componentToggle: !!document.querySelector('button[onclick*="toggle_component_diagram"]'),
            iflowSegments: window.__iflowSegments ? Object.keys(window.__iflowSegments).length : -1,
            renderScript: typeof window._renderDiagramsInContainer,
            bindLinks: typeof window._iflowBindLinks,
        };
        const blue = inline.flatMap(d => Array.from(d.querySelectorAll('text')).filter(t => (t.getAttribute('fill') || '').toLowerCase() === '#0000ff'));
        out.firstBlue = blue.length ? { text: blue[0].textContent, fill: blue[0].getAttribute('fill'), deco: blue[0].getAttribute('text-decoration'), cursor: getComputedStyle(blue[0]).cursor } : null;
        return out;
    });
    console.log('FACTS ' + JSON.stringify(facts));
    // Click the first blue link text as a reader would, and see whether a popup opens.
    const opened = await page.evaluate(async () => {
        const blue = Array.from(document.querySelectorAll('.plantuml-inline-svg text')).filter(t => (t.getAttribute('fill') || '').toLowerCase() === '#0000ff');
        if (!blue.length) return 'no blue text';
        blue[0].dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
        await new Promise(r => setTimeout(r, 500));
        return document.querySelector('.iflow-overlay') ? 'popup opened' : 'nothing opened';
    });
    console.log('CLICK ' + opened);
    // The embedded component diagram, shown as a reader would through its toggle.
    const comp = await page.evaluate(async () => {
        const btn = document.querySelector('button[onclick*="toggle_component_diagram"]');
        if (!btn) return 'no toggle';
        btn.click();
        await new Promise(r => setTimeout(r, 3000));
        const cd = document.getElementById('component-diagram');
        return cd ? ('display=' + cd.style.display + ' svg=' + !!cd.querySelector('svg') + ' text=' + JSON.stringify((cd.textContent || '').trim().slice(0, 80))) : 'no panel';
    });
    console.log('COMPONENT ' + comp);
    console.log('ERRORS ' + JSON.stringify(errors.slice(0, 5)));
    await browser.close();
})().catch(e => { console.log('PROBE FAILED ' + e.message); process.exit(1); });
