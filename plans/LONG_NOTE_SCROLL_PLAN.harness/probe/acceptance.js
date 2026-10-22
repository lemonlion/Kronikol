// R1's acceptance (plans/LONG_NOTE_SCROLL_PLAN.md 8.4, section 10): the product's own boxes in Chromium, Firefox and
// WebKit, and T21's 40-call report capped against drawn in full, from load to every part decorated. Prints facts only.
// The reports are the ones LongPayloadScrollTests T1 and T21 write to the E2E project's PlaywrightOutput directory.
// Usage: node acceptance.js <dir holding scroll-t1.html, scroll-t21-capped.html, scroll-t21-full.html> [box|timing]
const path = require('path');
const dir = process.argv[2];
const pw = require(process.env.KRONIKOL_PW || require('path').join(__dirname, 'pw', 'package'));
const { launchInstalled } = require(require('path').join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));
const url = f => 'file:///' + path.join(dir, f).replace(/\\/g, '/');

async function open(page, file) {
    const t0 = Date.now();
    await page.goto(url(file));
    await page.evaluate(() => {
        const sc = document.querySelector('details.scenario');
        const ft = sc && sc.closest('details.feature');
        if (ft) ft.open = true;
        if (sc) { sc.open = true; sc.scrollIntoView(); }
        if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body);
    });
    await page.waitForFunction(() => {
        const c = document.querySelector('[data-diagram-type="plantuml"]');
        if (!c || c._noteRendering || window._plantumlRendering) return false;
        const boxes = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
        return (boxes.length ? boxes : [c]).every(b => b.querySelector(':scope > svg .note-hover-rect'));
    }, null, { timeout: 180000, polling: 200 });
    return Date.now() - t0;
}

const helpers = () => {
    window.__a = {
        c: () => document.querySelector('[data-diagram-type="plantuml"]'),
        idx: (key) => window._parseNoteBlocks(document.querySelector('[data-diagram-type="plantuml"]')._noteOriginalSource)
            .findIndex(b => b.contentLines.some(l => l.indexOf('"' + key + '_0001"') >= 0)),
        group: (idx) => {
            let g = null;
            document.querySelectorAll('[data-diagram-type="plantuml"] svg').forEach(s => window._findNoteGroups(s).forEach(x => {
                const ix = window._noteIndicesOf(x); if (ix && ix.original === idx) g = x;
            }));
            return g;
        },
        box: (idx) => document.querySelector('.note-scroll-frame[data-note="' + idx + '"] .note-scroll'),
    };
};

async function boxFacts(browserType) {
    const browser = await launchInstalled(browserType, { headless: true });
    const page = await (await browser.newContext({ viewport: { width: 1600, height: 1000 } })).newPage();
    const out = { engine: browserType.name() };
    await open(page, 'scroll-t1.html');
    await page.evaluate(helpers);
    const idx = await page.evaluate(() => window.__a.idx('r1500'));
    await page.evaluate((i) => window.__a.group(i).paths[0].scrollIntoView({ block: 'center' }), idx);
    try {
        await page.waitForFunction((i) => !!window.__a.box(i), idx, { timeout: 60000, polling: 200 });
    } catch (e) {
        // what the page holds for the note when no box came
        out.noBox = await page.evaluate((i) => {
            const g = window.__a.group(i), p = g && g.paths[0], c = window.__a.c();
            const r = p ? p.getBoundingClientRect() : null;
            return { group: !!g, watched: !!(p && p._noteScrollMake), records: !!(c._noteScrollDrawn && c._noteScrollDrawn.records),
                rect: r ? [Math.round(r.top), Math.round(r.height)] : null, inner: window.innerHeight, io: typeof IntersectionObserver };
        }, idx);
        await browser.close();
        return out;
    }
    Object.assign(out, await page.evaluate((i) => {
        const c = window.__a.c(), b = window.__a.box(i);
        return { parts: c.querySelectorAll(':scope > .puml-fragment').length, continued: /Continued/.test(c.textContent),
            lines: b.textContent.split('\n').length, label: b.getAttribute('aria-label') };
    }, idx));
    // the wheel over the box scrolls the box, not the page
    const r = await page.evaluate((i) => { const f = window.__a.box(i).getBoundingClientRect(); return [f.left + f.width / 2, f.top + f.height / 2, window.scrollY]; }, idx);
    await page.mouse.move(r[0], r[1]);
    await page.mouse.wheel(0, 300);
    await page.waitForTimeout(600);
    Object.assign(out, await page.evaluate(([i, y]) => ({ wheelBox: window.__a.box(i).scrollTop > 0, wheelPage: window.scrollY !== y }), [idx, r[2]]));
    // keys on the focused box
    await page.evaluate((i) => { const b = window.__a.box(i); b.scrollTop = 0; b.focus(); }, idx);
    const keys = {};
    for (const k of ['ArrowDown', 'PageDown', 'End', 'ArrowUp', 'PageUp', 'Home']) {
        const before = await page.evaluate((i) => window.__a.box(i).scrollTop, idx);
        await page.keyboard.press(k);
        await page.waitForTimeout(400);
        const after = await page.evaluate((i) => ({ top: window.__a.box(i).scrollTop, focused: document.querySelectorAll('.scenario-focused').length }), idx);
        keys[k] = (after.top > before ? '+' : after.top < before ? '-' : '0') + (after.focused ? ' scenario moved' : '');
    }
    out.keys = keys;
    // a selection over three lines keeps its line breaks
    out.selection = await page.evaluate((i) => {
        // the box holds a text node per line (and its indent)
        const b = window.__a.box(i), range = document.createRange();
        const at = (needle) => {
            const w = document.createTreeWalker(b, NodeFilter.SHOW_TEXT);
            for (let n = w.nextNode(); n; n = w.nextNode()) { const k = n.data.indexOf(needle); if (k >= 0) return [n, k]; }
            return null;
        };
        const s = at('"r1500_0003"'), e = at('"r1500_0006"');
        if (!s || !e) return 'targets not found';
        range.setStart(s[0], s[1]); range.setEnd(e[0], e[1]);
        const sel = window.getSelection(); sel.removeAllRanges(); sel.addRange(range);
        return JSON.stringify(sel.toString()).slice(0, 90);
    }, idx);
    await browser.close();
    return out;
}

async function timing(browserType) {
    const browser = await launchInstalled(browserType, { headless: true });
    const page = await (await browser.newContext({ viewport: { width: 1920, height: 1080 } })).newPage();
    await open(page, 'scroll-t21-capped.html');
    const res = { engine: browserType.name(), full: [], capped: [] };
    for (let i = 0; i < 3; i++) {
        res.full.push(await open(page, 'scroll-t21-full.html'));
        res.capped.push(await open(page, 'scroll-t21-capped.html'));
    }
    res.parts = await page.evaluate(() => document.querySelector('[data-diagram-type="plantuml"]').querySelectorAll(':scope > .puml-fragment').length);
    await browser.close();
    return res;
}

// a fourth argument, box or timing, runs only that half
const mode = process.argv[3] || 'all';
(async () => {
    if (mode !== 'timing') for (const bt of [pw.chromium, pw.firefox, pw.webkit]) {
        try { console.log(JSON.stringify(await boxFacts(bt))); } catch (e) { console.log(JSON.stringify({ engine: bt.name(), error: e.message.slice(0, 200) })); }
    }
    if (mode !== 'box') for (const bt of [pw.chromium, pw.firefox, pw.webkit]) {
        try { console.log(JSON.stringify(await timing(bt))); } catch (e) { console.log(JSON.stringify({ engine: bt.name(), error: e.message.slice(0, 200) })); }
    }
})();
