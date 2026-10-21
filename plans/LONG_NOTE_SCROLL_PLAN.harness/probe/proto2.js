// In-report prototype: the report's own pipeline draws notes capped at 40 lines (mixed-capped-expanded.html); this
// lays a scroll box holding each capped note's full body over its drawn box and checks how every engine treats it.
// Usage: node proto2.js <chromium|firefox|webkit> <report.html> <full.puml> <out-prefix>
const pw = require(process.env.KRONIKOL_PW || require('path').join(__dirname, 'pw', 'package'));
const { launchInstalled } = require(require('path').join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));
const fs = require('fs');
const path = require('path');

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

const sleep = ms => new Promise(r => setTimeout(r, ms));

(async () => {
    const [engine = 'chromium', report, puml, outPrefix] = process.argv.slice(2);
    const full = fullBodies(fs.readFileSync(puml, 'utf8'));
    const browser = await launchInstalled(pw[engine]);
    const page = await browser.newPage({ viewport: { width: 1600, height: 1000 } });
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
    await sleep(500);

    // 1. attach
    Object.assign(out, await page.evaluate(full => {
        const t0 = performance.now();
        const boxes = [];
        let font = null;
        for (const svg of document.querySelectorAll('.plantuml-browser svg')) {
            for (const g of window._findNoteGroups(svg)) {
                const m = g.texts.map(t => t.textContent).join('').match(/"(\d{3})-\d{5}"/);
                if (!m || full[m[1]] === undefined) continue;
                const body = g.paths.reduce((a, p) => { const b = p.getBBox(); return b.width * b.height > a.width * a.height ? b : a; }, { width: 0, height: 0 });
                const ys = [...new Set(g.texts.map(t => +t.getAttribute('y')))].sort((a, b) => a - b);
                const pitch = ys.length > 2 ? ys[2] - ys[1] : 15;
                const head = g.texts.filter(t => +t.getAttribute('y') === ys[0]).map(t => t.getBBox());
                const headBottom = Math.max(...head.map(b => b.y + b.height));
                const sample = g.texts[g.texts.length - 1];
                font = { family: sample.getAttribute('font-family'), size: sample.getAttribute('font-size'), pitch };
                const fo = document.createElementNS('http://www.w3.org/2000/svg', 'foreignObject');
                const x = body.x + 6, y = headBottom + 2, w = body.width - 16, h = body.y + body.height - y - 4;
                fo.setAttribute('x', x); fo.setAttribute('y', y); fo.setAttribute('width', w); fo.setAttribute('height', h);
                const div = document.createElement('div');
                div.className = 'note-scroll-probe';
                div.style.cssText = 'box-sizing:border-box;width:100%;height:100%;overflow:auto;white-space:pre-wrap;overflow-wrap:anywhere;'
                    + 'margin:0;padding:0 2px;color:#000;font-family:' + font.family + ';font-size:' + font.size + 'px;line-height:' + pitch + 'px';
                div.textContent = full[m[1]];
                fo.appendChild(div);
                g.texts.forEach(t => { if (+t.getAttribute('y') !== ys[0]) t.style.visibility = 'hidden'; });
                g.texts[g.texts.length - 1].after(fo);
                const bodyPath = g.paths.reduce((a, p) => { const b = p.getBBox(); return !a || b.width * b.height > a._area ? Object.assign(p, { _area: b.width * b.height }) : a; }, null);
                boxes.push({ seed: m[1], div, fo, path: bodyPath, head: g.texts.filter(t => +t.getAttribute('y') === ys[0])[0], svg, g });
            }
        }
        window._probeBoxes = boxes;
        return { attached: boxes.length, attachMs: Math.round(performance.now() - t0), font };
    }, full));

    // pick a box with a long body, bring it to the middle of the screen
    const pick = await page.evaluate(() => {
        const boxes = window._probeBoxes;
        let i = boxes.findIndex(b => b.div.scrollHeight > 10000);
        if (i < 0) i = 0;
        window._probe = boxes[i];
        boxes[i].fo.scrollIntoView({ block: 'center' });
        return { index: i, seed: boxes[i].seed, scrollHeight: boxes[i].div.scrollHeight, clientHeight: boxes[i].div.clientHeight };
    });
    out.pick = pick;
    await sleep(300);

    const rect = () => page.evaluate(() => {
        const r = window._probe.div.getBoundingClientRect();
        const p = window._probe.path.getBoundingClientRect();
        return { x: r.x, y: r.y, w: r.width, h: r.height, px: p.x, py: p.y, pw: p.width, ph: p.height };
    });

    // 2. alignment under zoom: the box's place inside the note's drawn path, as fractions, at 3 widths
    out.zoom = [];
    for (const factor of [1, 2, 0.5]) {
        await page.evaluate(f => {
            const svg = window._probe.svg;
            if (!svg._naturalW) { svg.style.maxWidth = 'none'; svg.style.width = ''; svg._naturalW = svg.getBoundingClientRect().width; }
            svg.style.maxWidth = 'none';
            svg.style.width = (svg._naturalW * f) + 'px';
            window._probe.fo.scrollIntoView({ block: 'center' });
        }, factor);
        await sleep(200);
        const r = await rect();
        out.zoom.push({ factor, left: +((r.x - r.px) / r.pw).toFixed(4), top: +((r.y - r.py) / r.ph).toFixed(4), w: +(r.w / r.pw).toFixed(4), h: +(r.h / r.ph).toFixed(4) });
    }
    await page.evaluate(() => { const svg = window._probe.svg; svg.style.width = svg._naturalW + 'px'; window._probe.fo.scrollIntoView({ block: 'center' }); });
    await sleep(300);
    if (outPrefix) {
        const r = await rect();
        await page.screenshot({ path: `${outPrefix}-${engine}.png`, clip: { x: Math.max(0, r.px - 20), y: Math.max(0, r.py - 20), width: Math.min(900, r.pw + 40), height: Math.min(700, r.ph + 40) } });
    }

    // 3. wheel over the box, then at its end (scroll chaining), with and without overscroll-behavior: contain
    const scrolls = () => page.evaluate(() => ({ box: Math.round(window._probe.div.scrollTop), page: Math.round((document.scrollingElement || document.documentElement).scrollTop) }));
    out.wheel = {};
    for (const contain of [false, true]) {
        await page.evaluate(c => { window._probe.div.style.overscrollBehavior = c ? 'contain' : 'auto'; window._probe.div.scrollTop = 0; window._probe.fo.scrollIntoView({ block: 'center' }); }, contain);
        await sleep(300);
        let r = await rect();
        await page.mouse.move(r.x + r.w / 2, r.y + r.h / 2);
        const a = await scrolls();
        await page.mouse.wheel(0, 240);
        await sleep(700);
        const b = await scrolls();
        await page.evaluate(() => { const d = window._probe.div; d.scrollTop = d.scrollHeight; });
        await sleep(300);
        r = await rect();
        await page.mouse.move(r.x + r.w / 2, r.y + r.h / 2);
        const c = await scrolls();
        await page.mouse.wheel(0, 240);
        await sleep(700);
        const d = await scrolls();
        out.wheel[contain ? 'contain' : 'auto'] = { firstWheelMovesBox: b.box - a.box, firstWheelMovesPage: b.page - a.page, atEndWheelMovesBox: d.box - c.box, atEndWheelMovesPage: d.page - c.page };
    }

    // 4. Ctrl+wheel over the box: does the page's zoom still get it, and does the box scroll?
    await page.evaluate(() => { window._probe.div.scrollTop = 0; window._probe.fo.scrollIntoView({ block: 'center' }); });
    await sleep(300);
    {
        const r = await rect();
        await page.mouse.move(r.x + r.w / 2, r.y + r.h / 2);
        const w0 = await page.evaluate(() => window._probe.svg.getBoundingClientRect().width);
        const s0 = await scrolls();
        await page.keyboard.down('Control');
        await page.mouse.wheel(0, -240);
        await page.keyboard.up('Control');
        await sleep(500);
        const w1 = await page.evaluate(() => window._probe.svg.getBoundingClientRect().width);
        const s1 = await scrolls();
        out.ctrlWheel = { svgWidthBefore: Math.round(w0), svgWidthAfter: Math.round(w1), boxScrolled: s1.box - s0.box };
    }

    // 5. find in page for text far down the box
    out.find = await page.evaluate(() => {
        const d = window._probe.div;
        d.scrollTop = 0;
        const lines = d.textContent.split('\n');
        const target = (lines[Math.floor(lines.length * 0.7)].match(/"(\d{3}-\d{5})"/) || [])[1]
            || lines.slice(Math.floor(lines.length * 0.7)).join('\n').match(/"(\d{3}-\d{5})"/)[1];
        if (typeof window.find !== 'function') return { supported: false };
        window.getSelection().removeAllRanges();
        const found = window.find(target, true, false, false);
        const sel = window.getSelection();
        return { supported: true, target, found, boxScrollTop: Math.round(d.scrollTop), selectionInBox: !!(sel.anchorNode && d.contains(sel.anchorNode)) };
    });

    // 6. a selection inside the box keeps its line breaks
    out.selection = await page.evaluate(() => {
        const d = window._probe.div, t = d.firstChild, text = t.data;
        let from = 0;
        for (let i = 0; i < 10; i++) from = text.indexOf('\n', from) + 1;
        let to = from;
        for (let i = 0; i < 3; i++) to = text.indexOf('\n', to) + 1;
        const r = document.createRange(); r.setStart(t, from); r.setEnd(t, to - 1);
        const s = window.getSelection(); s.removeAllRanges(); s.addRange(r);
        const got = s.toString();
        return { lineBreaks: (got.match(/\n/g) || []).length, expected: 2, sample: got.slice(0, 60) };
    });

    // 7. keyboard: is the box focusable without a tabindex, and does ArrowDown scroll it once focused?
    out.keyboard = await page.evaluate(() => { const d = window._probe.div; d.scrollTop = 0; d.focus(); return { focusable: document.activeElement === d, tabIndex: d.tabIndex }; });
    if (!out.keyboard.focusable) out.keyboard.withTabindex0 = await page.evaluate(() => { const d = window._probe.div; d.tabIndex = 0; d.focus(); return document.activeElement === d; });
    if (!out.keyboard.focusable && out.keyboard.withTabindex0) out.keyboard.focusable = 'with tabindex=0';
    if (out.keyboard.focusable) {
        await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown');
        await sleep(400);
        out.keyboard.arrowScrolls = await page.evaluate(() => Math.round(window._probe.div.scrollTop));
        out.keyboard.stillFocused = await page.evaluate(() => document.activeElement === window._probe.div);
        await page.keyboard.press('PageDown'); await sleep(500);
        out.keyboard.pageDownScrolls = await page.evaluate(() => Math.round(window._probe.div.scrollTop));
    }

    // 8. hover: do the note's buttons show over the box, and over the drawn header row?
    await page.evaluate(() => { document.activeElement && document.activeElement.blur && document.activeElement.blur(); window._probe.fo.scrollIntoView({ block: 'center' }); });
    await sleep(300);
    const shown = () => page.evaluate(() => [...window._probe.svg.querySelectorAll('.note-toggle-icon')].filter(b => b.style.opacity === '1').length);
    {
        await page.mouse.move(5, 5); await sleep(500);
        const r = await rect();
        await page.mouse.move(r.x + r.w / 2, r.y + r.h / 2); await sleep(250);
        const overBox = await shown();
        await page.mouse.move(5, 5); await sleep(500);
        const hr = await page.evaluate(() => { const b = window._probe.head.getBoundingClientRect(); return { x: b.x + b.width / 2, y: b.y + b.height / 2 }; });
        await page.mouse.move(hr.x, hr.y); await sleep(250);
        const overHeader = await shown();
        out.hover = { buttonsShownOverBox: overBox, buttonsShownOverHeaderRow: overHeader };
    }

    // 9. right-click on the box (the helper the E2E suite uses dispatches the event the same way)
    out.contextMenu = await page.evaluate(() => {
        const d = window._probe.div, r = d.getBoundingClientRect();
        d.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: r.x + r.width / 2, clientY: r.y + r.height / 2, button: 2 }));
        const menu = document.querySelector('.diagram-ctx-menu');
        const items = menu ? [...menu.querySelectorAll('div')].map(e => e.childNodes.length === 1 && e.firstChild.nodeType === 3 ? e.textContent.trim() : null).filter(Boolean) : null;
        if (menu) menu.remove();
        return { menu: !!menu, items };
    });

    // 10. scrolling cost: every box scrolled each frame for 60 frames
    out.scrollFrames = await page.evaluate(async () => {
        for (let w = 0; w < 30; w++) await new Promise(r => requestAnimationFrame(r));
        const frames = [];
        const boxes = window._probeBoxes;
        for (let f = 0; f < 60; f++) {
            frames.push(await new Promise(r => requestAnimationFrame(r)));
            boxes.forEach(b => { b.div.scrollTop = (b.div.scrollHeight - b.div.clientHeight) * (f / 59); });
        }
        const gaps = frames.slice(1).map((t, i) => t - frames[i]);
        return { boxes: boxes.length, avg: +(gaps.reduce((a, b) => a + b, 0) / gaps.length).toFixed(1), max: Math.round(Math.max(...gaps)) };
    });

    console.log(JSON.stringify(out));
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
