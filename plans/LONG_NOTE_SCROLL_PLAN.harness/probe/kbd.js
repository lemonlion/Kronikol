// Keyboard on a focused scroll box, with the exemption the plan gives it: the report's document keydown handler moves
// between scenarios on Up and Down, so the box stops those keys from reaching it (as the plan's exemption would).
// Usage: node kbd.js <chromium|firefox|webkit> <report.html> <full.puml>
const pw = require(process.env.KRONIKOL_PW || require('path').join(__dirname, 'pw', 'package'));
const { launchInstalled } = require(require('path').join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));
const fs = require('fs');
const path = require('path');
const sleep = ms => new Promise(r => setTimeout(r, ms));

function fullBodies(src) {
    const lines = src.replace(/\r\n/g, '\n').split('\n');
    const full = {};
    for (let i = 0; i < lines.length; i++) {
        if (!/^note\s+right$/.test(lines[i].trim())) continue;
        let j = i + 1;
        const body = [];
        while (lines[j].trim() !== 'end note') body.push(lines[j++]);
        const m = body.join('\n').match(/"id": "(\d{3})-/);
        if (m && body.length > 40) full[m[1]] = body.slice(2).join('\n');
        i = j;
    }
    return full;
}

(async () => {
    const [engine = 'chromium', report, puml] = process.argv.slice(2);
    const full = fullBodies(fs.readFileSync(puml, 'utf8'));
    const browser = await launchInstalled(pw[engine]);
    const page = await browser.newPage({ viewport: { width: 1400, height: 900 } });
    await page.goto('file:///' + path.resolve(report).replace(/\\/g, '/'));
    await page.evaluate(() => {
        document.querySelectorAll('details').forEach(d => { d.open = true; });
        if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body);
    });
    await page.waitForFunction(() => {
        const c = document.querySelector('.plantuml-browser');
        return c && c.querySelector('svg') && !window._plantumlRendering;
    }, null, { polling: 200, timeout: 120000 });
    await sleep(500);
    await page.evaluate(full => {
        const svg = document.querySelector('.plantuml-browser svg');
        for (const g of window._findNoteGroups(svg)) {
            const m = g.texts.map(t => t.textContent).join('').match(/"(\d{3})-\d{5}"/);
            if (!m || full[m[1]] === undefined || window._kbd) continue;
            const body = g.paths.reduce((a, p) => { const b = p.getBBox(); return b.width * b.height > a.width * a.height ? b : a; }, { width: 0, height: 0 });
            const ys = [...new Set(g.texts.map(t => +t.getAttribute('y')))].sort((a, b) => a - b);
            const head = g.texts.filter(t => +t.getAttribute('y') === ys[0]).map(t => t.getBBox());
            const y = Math.max(...head.map(b => b.y + b.height)) + 2;
            const fo = document.createElementNS('http://www.w3.org/2000/svg', 'foreignObject');
            fo.setAttribute('x', body.x + 6); fo.setAttribute('y', y); fo.setAttribute('width', body.width - 16); fo.setAttribute('height', body.y + body.height - y - 4);
            const div = document.createElement('div');
            div.tabIndex = 0;
            div.style.cssText = 'box-sizing:border-box;width:100%;height:100%;overflow:auto;white-space:pre-wrap;font:13px sans-serif;line-height:15px';
            div.textContent = full[m[1]];
            // the exemption: keys meant for the box stop here, before the document's scenario navigation sees them
            div.addEventListener('keydown', e => { if (/^(ArrowUp|ArrowDown|PageUp|PageDown|Home|End| )$/.test(e.key)) e.stopPropagation(); });
            fo.appendChild(div);
            g.texts[g.texts.length - 1].after(fo);
            window._kbd = div;
        }
        window._kbd.scrollIntoView({ block: 'center' });
    }, full);
    await sleep(300);
    // reach the box with Tab from the element before it, as a keyboard user would
    await page.evaluate(() => { const d = window._kbd; d.scrollTop = 0; d.focus(); });
    const focused = await page.evaluate(() => document.activeElement === window._kbd);
    const read = () => page.evaluate(() => Math.round(window._kbd.scrollTop));
    const out = { engine, focused };
    for (const key of ['ArrowDown', 'ArrowDown', 'PageDown', 'ArrowUp', 'PageUp', 'End', 'Home']) {
        const before = await read();
        await page.keyboard.press(key);
        await sleep(450);
        out[key + (out[key] !== undefined ? '2' : '')] = (await read()) - before;
    }
    out.scenarioFocusMoved = await page.evaluate(() => !!document.querySelector('.scenario-focused'));
    console.log(JSON.stringify(out));
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
