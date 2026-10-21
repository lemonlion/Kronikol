// The browser's own cost of scroll boxes inside an SVG, apart from Kronikol and the engine: N boxes of L lines laid in a
// tall SVG like capped notes, timed to attach (create, fill, first layout), to paint, and to scroll.
// Usage: node scale.js <chromium|firefox|webkit> [wrap=pre-wrap|pre]
const pw = require(process.env.KRONIKOL_PW || require('path').join(__dirname, 'pw', 'package'));
const { launchInstalled } = require(require('path').join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));
const sleep = ms => new Promise(r => setTimeout(r, ms));

function jsonLines(n, seed) {
    const out = ['{', '  "items": ['];
    for (let i = 0; out.length < n - 2; i++) {
        out.push('    {', `      "id": "${String(seed).padStart(3, '0')}-${String(i).padStart(5, '0')}",`,
            `      "name": "Breakfast item number ${i} with a longer descriptive name",`, `      "price": ${(i * 37 % 1000) / 100},`,
            '      "tags": ["warm", "sweet", "batch-' + (i % 7) + '"]', '    },');
    }
    out.push('  ]', '}');
    return out.slice(0, n).join('\n');
}

(async () => {
    const [engine = 'chromium', wrap = 'pre-wrap', mode = 'force'] = process.argv.slice(2);
    const browser = await launchInstalled(pw[engine]);
    const page = await browser.newPage({ viewport: { width: 1600, height: 1000 } });
    await page.setContent('<!doctype html><body style="margin:0"></body>');
    const results = [];
    for (const [n, lines] of [[16, 150], [16, 1500], [100, 150], [100, 1500], [400, 150], [1, 10000], [1, 50000], [1, 200000]]) {
        const texts = Array.from({ length: Math.min(n, 8) }, (_, i) => jsonLines(lines, i));
        const r = await page.evaluate(async ({ n, texts, wrap, mode }) => {
            document.body.innerHTML = '';
            const pitch = 640; // one capped note (40 rows of 15 px) and a message between notes
            const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            svg.setAttribute('viewBox', `0 0 900 ${n * pitch + 40}`);
            svg.setAttribute('width', 900); svg.setAttribute('height', n * pitch + 40);
            for (let i = 0; i < n; i++) {
                const rect = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
                rect.setAttribute('x', 100); rect.setAttribute('y', 20 + i * pitch); rect.setAttribute('width', 700); rect.setAttribute('height', 600);
                rect.setAttribute('fill', '#fefece'); rect.setAttribute('stroke', '#999');
                svg.appendChild(rect);
            }
            document.body.appendChild(svg);
            await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
            const heap0 = performance.memory ? performance.memory.usedJSHeapSize : null;
            const t0 = performance.now();
            const divs = [];
            for (let i = 0; i < n; i++) {
                const fo = document.createElementNS('http://www.w3.org/2000/svg', 'foreignObject');
                fo.setAttribute('x', 106); fo.setAttribute('y', 40 + i * pitch); fo.setAttribute('width', 684); fo.setAttribute('height', 574);
                const div = document.createElement('div');
                div.style.cssText = `box-sizing:border-box;width:100%;height:100%;overflow:auto;white-space:${wrap};overflow-wrap:anywhere;font:13px sans-serif;line-height:15px;overscroll-behavior:contain` + (mode === 'cv' ? ';content-visibility:auto' : '');
                div.tabIndex = 0;
                if (mode === 'io') { div._fill = texts[i % texts.length]; }
                else div.textContent = texts[i % texts.length];
                fo.appendChild(div);
                svg.appendChild(fo);
                divs.push(div);
            }
            if (mode === 'io') {
                // the plan's lazy fill: a box gets its text when it comes within a screen of the window
                const io = new IntersectionObserver(es => es.forEach(e => {
                    if (e.isIntersecting && e.target._fill !== undefined) { e.target.textContent = e.target._fill; delete e.target._fill; io.unobserve(e.target); }
                }), { rootMargin: '100% 0px' });
                divs.forEach(d => io.observe(d));
            }
            const sh = mode === 'force' ? divs[0].scrollHeight : -1; // forces layout of what is laid out
            const attachMs = performance.now() - t0;
            const tp = performance.now();
            await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
            if (mode === 'io') await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
            const paintMs = performance.now() - tp;
            const filled = divs.filter(d => d._fill === undefined).length;
            const heap1 = performance.memory ? performance.memory.usedJSHeapSize : null;
            // scroll: the boxes on screen (the first two) scrolled each frame, and once all of them
            const frames = [];
            for (let f = 0; f < 60; f++) {
                frames.push(await new Promise(r => requestAnimationFrame(r)));
                for (let i = 0; i < Math.min(2, n); i++) divs[i].scrollTop = (divs[i].scrollHeight - divs[i].clientHeight) * (f / 59);
            }
            const gaps = frames.slice(1).map((t, i) => t - frames[i]);
            const ta = performance.now();
            divs.forEach(d => { d.scrollTop = d.scrollHeight / 2; });
            await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
            const allMs = performance.now() - ta;
            return { n, scrollHeight: sh, attachMs: Math.round(attachMs), paintMs: Math.round(paintMs),
                onScreenFrameAvg: +(gaps.reduce((a, b) => a + b, 0) / gaps.length).toFixed(1), onScreenFrameMax: Math.round(Math.max(...gaps)),
                scrollAllOnceMs: Math.round(allMs), filled, heapMB: heap0 === null ? null : +((heap1 - heap0) / 1048576).toFixed(1) };
        }, { n, texts, wrap, mode });
        results.push({ lines, ...r });
    }
    console.log(engine, wrap, mode);
    for (const r of results) console.log(JSON.stringify(r));
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
