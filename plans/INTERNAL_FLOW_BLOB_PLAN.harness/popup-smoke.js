// Does an arrow in a real report open its popup, and does the popup draw? Before or after the plan.
//
//   node popup-smoke.js <TestRunReport.html or https URL> [diagrams=3]
//
// The consumer check of §8.6: run it on a lane's report before the upgrade and after it, and the two
// outputs must agree on every count. Loads the report in headless Chromium (network allowed: under
// BrowserJs the engine comes from the CDN), reads which arrow ids have a segment the way the report's
// own scripts do (the `window.__iflowSegments` object before the plan, `_iflowHasSegment` after it),
// then, for the first `diagrams` diagrams that link a segment: opens the diagram, waits for it to
// draw, counts the link texts the render script bound, clicks the first one (a dispatched event,
// the E2E rule for SVG), and times the popup and the activity diagram inside it. Prints one JSON line
// per diagram and a summary with every console error the page logged.
//
// Uses the E2E project's own Playwright driver, like load-timing.js.
'use strict';
const path = require('path');

const target = process.argv[2];
const wanted = parseInt(process.argv[3] || '3', 10);
if (!target) { console.error('usage: node popup-smoke.js <TestRunReport.html | URL> [diagrams]'); process.exit(2); }
const url = /^https?:\/\//.test(target) ? target : 'file:///' + path.resolve(target).replace(/\\/g, '/');
const driver = path.resolve(__dirname, '..', '..', 'tests', 'Kronikol.Tests.EndToEnd', 'bin', 'Debug', 'net10.0', '.playwright', 'package');
const { chromium } = require(driver);

(async () => {
    const browser = await chromium.launch();
    const page = await browser.newPage({ viewport: { width: 1400, height: 900 } });
    const errors = [];
    page.on('pageerror', e => errors.push('pageerror: ' + e.message));
    page.on('console', m => { if (m.type() === 'error') errors.push('console: ' + m.text()); });
    const t0 = Date.now();
    await page.goto(url, { waitUntil: 'load', timeout: 120000 });
    const loadMs = Date.now() - t0;
    await page.waitForFunction(() => typeof window._iflowShowPopup === 'function' && typeof window._getPumlZ === 'function',
        null, { timeout: 60000, polling: 200 });
    // Count the decodes of the segment blob itself (not the per-diagram ones, which go through the same
    // helper): the scripts resolve the global at call time, so a wrapper installed now sees every call.
    await page.evaluate(() => {
        const el = document.getElementById('iflow-segments');
        const z = el ? (JSON.parse(el.textContent).z || null) : null;
        window.__smokeBlobDecodes = z ? 0 : null;
        if (!z || typeof window.decompressGzipBase64 !== 'function') return;
        const original = window.decompressGzipBase64;
        window.decompressGzipBase64 = function (arg) {
            if (arg === z) window.__smokeBlobDecodes++;
            return original.apply(this, arguments);
        };
    });

    // Which diagrams link a segment, decided the way the page decides it.
    const plan = await page.evaluate(async (wanted) => {
        const form = document.getElementById('iflow-segments') ? 'element'
            : (window.__iflowSegments && typeof window.__iflowSegments === 'object') ? 'literal' : 'none';
        const has = id => typeof window._iflowHasSegment === 'function' ? window._iflowHasSegment(id)
            : !!(window.__iflowSegments && window.__iflowSegments[id]);
        const data = JSON.parse(document.getElementById('puml-data').textContent);
        const generator = (document.querySelector('meta[name="generator"]') || {}).content || '';
        const picked = [];
        let scanned = 0, linked = 0, dataless = 0;
        for (const key of Object.keys(data)) {
            const source = await decompressGzipBase64(data[key]);
            const ids = [...new Set([...source.matchAll(/\[\[#(iflow-[^\s\]]+)/g)].map(m => m[1]))];
            scanned++;
            const withData = ids.filter(has);
            linked += withData.length;
            dataless += ids.length - withData.length;
            if (withData.length > 0 && picked.length < wanted * 4 && document.getElementById(key))
                picked.push({ key, links: ids.length, withData: withData.length });
        }
        return { form, generator, scanned, linked, dataless, picked };
    }, wanted);

    const results = [];
    let hidden = 0;
    for (const d of plan.picked) {
        if (results.length >= wanted) break;
        const r = await page.evaluate(async ({ key }) => {
            const el = document.getElementById(key);
            for (let p = el.parentElement; p; p = p.parentElement) if (p.tagName === 'DETAILS') p.open = true;
            if (el.offsetParent === null) return { hidden: true };   // in a tab or section not shown
            el.scrollIntoView({ block: 'center' });
            const until = async (cond, ms) => {
                const end = performance.now() + ms;
                while (performance.now() < end) { const v = cond(); if (v) return v; await new Promise(r => setTimeout(r, 200)); }
                return null;
            };
            const t0 = performance.now();
            const svg = await until(() => el.querySelector('svg'), 90000);
            if (!svg) return { drawn: false };
            const drawMs = performance.now() - t0;
            await new Promise(r => setTimeout(r, 300));   // bindIflowLinks runs in the same task as the insert; margin for fragments
            const bound = [...el.querySelectorAll('text, a')].filter(n => n.style.pointerEvents === 'all' || n.classList.contains('iflow-link-hover') || n.style.cursor === 'pointer');
            if (bound.length === 0) return { drawn: true, drawMs, bound: 0 };
            document.querySelectorAll('.iflow-overlay').forEach(o => o.remove());
            // Timed by a MutationObserver, not by polling: the popup may appear in a later task (after the
            // decode) and a 200 ms poll would measure the poll.
            let popupAt = null, contentAt = null;
            const settled = p => p.querySelector('svg') || p.querySelector('.iflow-no-data') || /could not be decompressed/.test(p.textContent);
            const watch = () => {
                const p = document.querySelector('.iflow-popup');
                if (p && popupAt === null) popupAt = performance.now();
                if (p && contentAt === null && settled(p)) contentAt = performance.now();
            };
            const mo = new MutationObserver(watch);
            mo.observe(document.body, { childList: true, subtree: true });
            const hits0 = window.plantuml && window.plantuml.cacheStats ? window.plantuml.cacheStats().hits : null;
            const c0 = performance.now();
            bound[0].dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
            watch();
            await until(() => contentAt !== null, 60000);
            mo.disconnect();
            const p = document.querySelector('.iflow-popup');
            return {
                drawn: true, drawMs: Math.round(drawMs), bound: bound.length,
                popupMs: popupAt == null ? null : Math.round(popupAt - c0),
                popupContentMs: contentAt == null ? null : Math.round(contentAt - c0),
                renderCacheHit: hits0 === null ? null : window.plantuml.cacheStats().hits > hits0,
                popupHasDiagram: !!(p && p.querySelector('svg')),
                popupNoData: !!(p && p.querySelector('.iflow-no-data')),
                popupFailure: !!(p && /could not be decompressed/.test(p.textContent)),
            };
        }, d);
        if (r.hidden) { hidden++; continue; }
        results.push({ ...d, ...r });
        console.log(JSON.stringify({ key: d.key, ...r, links: d.links, withData: d.withData }));
    }

    console.log(JSON.stringify({
        url: target, generator: plan.generator, form: plan.form, loadMs,
        diagramsScanned: plan.scanned, linkIdsWithSegment: plan.linked, linkIdsWithout: plan.dataless,
        checked: results.length, skippedHidden: hidden,
        blobDecodes: await page.evaluate(() => window.__smokeBlobDecodes),   // null: no blob on the page
        ok: results.length > 0 && results.every(r => r.drawn && r.bound > 0 && r.popupHasDiagram),
        errors: errors.length, firstErrors: errors.slice(0, 3),
    }));
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
