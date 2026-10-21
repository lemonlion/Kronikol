// Prototype: cap every note longer than CAP lines in the source (the engine lays out CAP lines), then lay a
// scrollable viewport holding the note's full text over the drawn box. Bundled Chromium only.
// Usage: node proto.js <report.html> <source.puml> [cap=40] [mode=fo|none] [screenshot.png]
const pw = require(process.env.KRONIKOL_PW || require('path').join(__dirname, 'pw', 'package'));
const { launchInstalled } = require(require('path').join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));
const fs = require('fs');
const path = require('path');

function capSource(source, cap) {
    const lines = source.split('\n');
    const out = [];
    const full = {};
    for (let i = 0; i < lines.length; i++) {
        const t = lines[i].trim();
        if (/^note\s+(left|right)$/.test(t)) {
            let j = i + 1;
            const body = [];
            while (lines[j].trim() !== 'end note') body.push(lines[j++]);
            out.push(lines[i]);
            if (body.length > cap) {
                const id = (body.join('\n').match(/"id": "(\d{3})-/) || [])[1];
                full[id] = body.slice(2).join('\n'); // the payload, without the header line and its blank
                for (let k = 0; k < cap; k++) out.push(body[k]);
            } else body.forEach(l => out.push(l));
            out.push('end note');
            i = j;
        } else out.push(lines[i]);
    }
    return { source: out.join('\n'), full };
}

(async () => {
    const [report, puml, capArg, mode = 'fo', shot] = process.argv.slice(2);
    const cap = parseInt(capArg || '40', 10);
    const { source, full } = capSource(fs.readFileSync(puml, 'utf8').replace(/\r\n/g, '\n'), cap);
    const browser = await launchInstalled(pw.chromium);
    const page = await browser.newPage({ viewport: { width: 1920, height: 1080 } });
    await page.goto('file:///' + path.resolve(report).replace(/\\/g, '/'));
    await page.waitForFunction(() => window.plantuml && window.plantuml.render && window._findNoteGroups, null, { polling: 200, timeout: 120000 });
    const result = await page.evaluate(async ({ source, full, mode }) => {
        const parts = window._splitWithChunkedNotes(source).length;
        const host = document.createElement('div');
        host.id = 'proto-host';
        document.body.prepend(host);
        const t0 = performance.now();
        window.plantuml.render(source.split('\n'), host.id);
        await new Promise(res => { const p = setInterval(() => { if (host.querySelector('svg')) { clearInterval(p); res(); } }, 20); });
        const drawMs = performance.now() - t0;
        const svg = host.querySelector('svg');
        const t1 = performance.now();
        const groups = window._findNoteGroups(svg);
        const findMs = performance.now() - t1;
        const t2 = performance.now();
        let attached = 0;
        if (mode === 'fo') {
            for (const g of groups) {
                const text = g.texts.map(t => t.textContent).join(' ');
                const m = text.match(/"(\d{3})-\d{5}"/);
                if (!m || full[m[1]] === undefined) continue;
                const body = g.paths.reduce((a, p) => { const b = p.getBBox(); return b.width * b.height > a.width * a.height ? b : a; }, { width: 0, height: 0 });
                const fo = document.createElementNS('http://www.w3.org/2000/svg', 'foreignObject');
                // below the header row, inside the fold's margin
                const top = Math.min(...g.texts.map(t => t.getBBox().y));
                fo.setAttribute('x', body.x + 6); fo.setAttribute('y', top + 22);
                fo.setAttribute('width', body.width - 18); fo.setAttribute('height', body.y + body.height - top - 28);
                const div = document.createElement('div');
                div.className = 'note-scroll';
                div.style.cssText = 'height:100%;overflow:auto;white-space:pre-wrap;font:13px "Courier New",monospace;line-height:15px;background:inherit;overscroll-behavior:contain';
                div.textContent = full[m[1]];
                fo.appendChild(div);
                const ink = g.texts.slice(1); // keep the header row painted; the viewport covers the rest
                ink.forEach(t => { t.style.visibility = 'hidden'; });
                g.paths[0].parentNode.appendChild(fo);
                attached++;
            }
        }
        const attachMs = performance.now() - t2;
        // let the first paint of the new svg and viewports settle before timing the scroll
        for (let w = 0; w < 30; w++) await new Promise(r => requestAnimationFrame(r));
        // scrolling: every viewport scrolled through 60 frames, long tasks counted
        let longTasks = 0;
        const po = new PerformanceObserver(l => { longTasks += l.getEntries().length; });
        try { po.observe({ entryTypes: ['longtask'] }); } catch (e) {}
        const frames = [];
        const views = [...host.querySelectorAll('.note-scroll')];
        for (let f = 0; f < 60; f++) {
            const ts = await new Promise(r => requestAnimationFrame(r));
            frames.push(ts);
            views.forEach(v => { v.scrollTop = (v.scrollHeight - v.clientHeight) * (f / 59); });
        }
        po.disconnect();
        const gaps = frames.slice(1).map((t, i) => t - frames[i]);
        return {
            parts, drawMs: Math.round(drawMs), findMs: Math.round(findMs), attachMs: Math.round(attachMs), attached,
            svgHeight: Math.round(svg.getBoundingClientRect().height), texts: svg.querySelectorAll('text').length,
            frameMsMax: Math.round(Math.max(...gaps)), frameMsAvg: +(gaps.reduce((a, b) => a + b, 0) / gaps.length).toFixed(1), longTasks,
            scrollHeights: views.slice(0, 3).map(v => v.scrollHeight),
        };
    }, { source, full, mode });
    console.log(JSON.stringify({ cap, mode, capped: Object.keys(full).length, ...result }));
    if (shot) {
        const el = await page.$('#proto-host .note-scroll');
        if (el) { await el.evaluate(e => { e.scrollTop = 200; e.scrollIntoView({ block: 'center' }); }); await page.screenshot({ path: shot }); }
    }
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
