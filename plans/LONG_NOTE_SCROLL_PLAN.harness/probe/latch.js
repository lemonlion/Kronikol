// Scroll trap check: the reader scrolls the PAGE with the wheel, the pointer held still, and a note's scroll box
// passes under the pointer mid-gesture. Does the gesture keep scrolling the page (latching), or does the box take it?
// Usage: node latch.js <chromium|firefox|webkit> <report.html> <full.puml> [gapMs=16]
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
    const [engine = 'chromium', report, puml, gapArg] = process.argv.slice(2);
    const gap = parseInt(gapArg || '16', 10);
    const full = fullBodies(fs.readFileSync(puml, 'utf8'));
    const browser = await launchInstalled(pw[engine]);
    const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
    await page.goto('file:///' + path.resolve(report).replace(/\\/g, '/'));
    await page.evaluate(() => {
        document.querySelectorAll('details').forEach(d => { d.open = true; });
        if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body);
    });
    await page.waitForFunction(() => {
        const els = [...document.querySelectorAll('.plantuml-browser')].flatMap(c => {
            const f = c.querySelectorAll(':scope > .puml-fragment');
            return f.length ? [...f] : [c];
        });
        return els.length > 0 && els.every(e => e.querySelector('svg')) && !window._plantumlRendering;
    }, null, { polling: 200, timeout: 300000 });
    await sleep(500);
    // boxes over every capped note; remember the first one
    const first = await page.evaluate(full => {
        const boxes = [];
        for (const svg of document.querySelectorAll('.plantuml-browser svg')) {
            for (const g of window._findNoteGroups(svg)) {
                const m = g.texts.map(t => t.textContent).join('').match(/"(\d{3})-\d{5}"/);
                if (!m || full[m[1]] === undefined) continue;
                const body = g.paths.reduce((a, p) => { const b = p.getBBox(); return b.width * b.height > a.width * a.height ? b : a; }, { width: 0, height: 0 });
                const ys = [...new Set(g.texts.map(t => +t.getAttribute('y')))].sort((a, b) => a - b);
                const head = g.texts.filter(t => +t.getAttribute('y') === ys[0]).map(t => t.getBBox());
                const y = Math.max(...head.map(b => b.y + b.height)) + 2;
                const fo = document.createElementNS('http://www.w3.org/2000/svg', 'foreignObject');
                fo.setAttribute('x', body.x + 6); fo.setAttribute('y', y); fo.setAttribute('width', body.width - 16); fo.setAttribute('height', body.y + body.height - y - 4);
                const div = document.createElement('div');
                div.style.cssText = 'box-sizing:border-box;width:100%;height:100%;overflow:auto;white-space:pre-wrap;font:13px sans-serif;line-height:15px';
                div.textContent = full[m[1]];
                fo.appendChild(div);
                g.texts.forEach(t => { if (+t.getAttribute('y') !== ys[0]) t.style.visibility = 'hidden'; });
                g.texts[g.texts.length - 1].after(fo);
                boxes.push(div);
            }
        }
        window._latchBoxes = boxes;
        const r = boxes[0].getBoundingClientRect();
        return { boxes: boxes.length, boxCentreX: r.x + r.width / 2, boxTopDoc: r.top + window.scrollY };
    }, full);
    // put the box's top 300 px below the bottom of the window, pointer at the box's x, 450 px down the window
    await page.evaluate(top => window.scrollTo(0, Math.max(0, top - 900 - 300)), first.boxTopDoc);
    await sleep(400);
    await page.mouse.move(first.boxCentreX, 450);
    const before = await page.evaluate(() => ({ page: Math.round(window.scrollY), box: Math.round(window._latchBoxes[0].scrollTop) }));
    // one continuous gesture: 40 notches of 100 px, gap ms apart (an ordinary wheel is 16-50 ms between notches)
    for (let i = 0; i < 40; i++) { await page.mouse.wheel(0, 100); await sleep(gap); }
    await sleep(700);
    const after = await page.evaluate(() => {
        const r = window._latchBoxes[0].getBoundingClientRect();
        return { page: Math.round(window.scrollY), box: Math.round(window._latchBoxes[0].scrollTop), boxUnderPointer: r.top < 450 && r.bottom > 450 };
    });
    // a fresh gesture after a pause, pointer still over the box
    await sleep(1500);
    await page.mouse.wheel(0, 100);
    await sleep(600);
    const fresh = await page.evaluate(() => ({ page: Math.round(window.scrollY), box: Math.round(window._latchBoxes[0].scrollTop) }));
    console.log(JSON.stringify({ engine, gap, pageMoved: after.page - before.page, boxMoved: after.box - before.box, boxUnderPointerAtEnd: after.boxUnderPointer,
        freshGesture: { pageMoved: fresh.page - after.page, boxMoved: fresh.box - after.box } }));
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
