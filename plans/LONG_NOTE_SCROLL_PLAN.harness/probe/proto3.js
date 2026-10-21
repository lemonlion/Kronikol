// Narrow-window phase of the in-report prototype: the diagram is wider than its column, so the page offers its zoom
// slider, Ctrl+wheel zoom and drag-to-pan. Checks what each does when the pointer is over a note's scroll box.
// Usage: node proto3.js <chromium|firefox|webkit> <report.html> <full.puml>
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
    const page = await browser.newPage({ viewport: { width: 640, height: 900 } });
    const out = { engine };
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
    await sleep(800);
    out.setup = await page.evaluate(full => {
        let probe = null;
        for (const svg of document.querySelectorAll('.plantuml-browser svg')) {
            for (const g of window._findNoteGroups(svg)) {
                const m = g.texts.map(t => t.textContent).join('').match(/"(\d{3})-\d{5}"/);
                if (!m || full[m[1]] === undefined || probe) continue;
                const body = g.paths.reduce((a, p) => { const b = p.getBBox(); return b.width * b.height > a.width * a.height ? b : a; }, { width: 0, height: 0 });
                const ys = [...new Set(g.texts.map(t => +t.getAttribute('y')))].sort((a, b) => a - b);
                const head = g.texts.filter(t => +t.getAttribute('y') === ys[0]).map(t => t.getBBox());
                const y = Math.max(...head.map(b => b.y + b.height)) + 2;
                const fo = document.createElementNS('http://www.w3.org/2000/svg', 'foreignObject');
                fo.setAttribute('x', body.x + 6); fo.setAttribute('y', y); fo.setAttribute('width', body.width - 16); fo.setAttribute('height', body.y + body.height - y - 4);
                const div = document.createElement('div');
                div.style.cssText = 'box-sizing:border-box;width:100%;height:100%;overflow:auto;white-space:pre-wrap;font:13px sans-serif;line-height:15px;overscroll-behavior:contain';
                div.textContent = full[m[1]];
                fo.appendChild(div);
                g.texts.forEach(t => { if (+t.getAttribute('y') !== ys[0]) t.style.visibility = 'hidden'; });
                g.texts[g.texts.length - 1].after(fo);
                const container = svg.closest('[data-diagram-type]') || svg.parentElement;
                probe = { div, fo, svg, container };
            }
        }
        window._probe = probe;
        const slider = probe.container.querySelector('.diagram-zoom-slider');
        return { container: probe.container.className, slider: !!slider, sliderValue: slider && slider.value, sliderMin: slider && slider.min, svgWidth: Math.round(probe.svg.getBoundingClientRect().width) };
    }, full);

    // Ctrl+wheel down over the box: the page's zoom (zoom out) or the box?
    await page.evaluate(() => window._probe.fo.scrollIntoView({ block: 'center' }));
    await sleep(300);
    let r = await page.evaluate(() => { const b = window._probe.div.getBoundingClientRect(); return { x: b.x + Math.min(b.width, 300) / 2, y: b.y + b.height / 2 }; });
    await page.mouse.move(r.x, r.y);
    const before = await page.evaluate(() => ({ w: Math.round(window._probe.svg.getBoundingClientRect().width), box: window._probe.div.scrollTop }));
    await page.keyboard.down('Control');
    await page.mouse.wheel(0, -240);
    await page.keyboard.up('Control');
    await sleep(500);
    const after = await page.evaluate(() => ({ w: Math.round(window._probe.svg.getBoundingClientRect().width), box: window._probe.div.scrollTop }));
    out.ctrlWheelUp = { svgWidthBefore: before.w, svgWidthAfter: after.w, boxScrolled: after.box - before.box };

    // zoom to 100% (natural size, wider than the column), then drag inside the box: pan or select?
    out.natural = await page.evaluate(() => {
        const s = window._probe.container.querySelector('.diagram-zoom-slider');
        if (!s) return { slider: false };
        s.value = 100;
        s.dispatchEvent(new Event('input', { bubbles: true }));
        s.dispatchEvent(new Event('change', { bubbles: true }));
        return { slider: true, naturalClass: window._probe.container.classList.contains('diagram-natural-size'), svgWidth: Math.round(window._probe.svg.getBoundingClientRect().width) };
    });
    await sleep(300);
    await page.evaluate(() => { window._probe.fo.scrollIntoView({ block: 'center', inline: 'nearest' }); window.getSelection().removeAllRanges(); });
    await sleep(300);
    r = await page.evaluate(() => { const b = window._probe.div.getBoundingClientRect(); const c = window._probe.container.getBoundingClientRect(); const x = Math.max(b.x, c.x) + 40; return { x, y: b.y + 60, cl: window._probe.container.scrollLeft }; });
    await page.mouse.move(r.x, r.y);
    await page.mouse.down();
    await page.mouse.move(r.x + 120, r.y + 45, { steps: 6 });
    await page.mouse.up();
    await sleep(300);
    out.dragInBox = await page.evaluate(cl0 => ({
        naturalClass: window._probe.container.classList.contains('diagram-natural-size'),
        containerPannedBy: Math.round(window._probe.container.scrollLeft - cl0),
        selectedChars: window.getSelection().toString().length,
        selectionInBox: !!(window.getSelection().anchorNode && window._probe.div.contains(window.getSelection().anchorNode)),
    }), r.cl);
    console.log(JSON.stringify(out));
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
