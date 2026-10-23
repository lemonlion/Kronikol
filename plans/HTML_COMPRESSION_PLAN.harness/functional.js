// Functional checks of a rebuilt (document.write) report page against the original, in Playwright's bundled engines.
// node functional.js <variants dir> <variant csv> [browsers csv]
const { chromium, firefox, webkit } = require('playwright-core');
const { pathToFileURL } = require('url');
const path = require('path');

const dir = process.argv[2];
const variants = process.argv[3].split(',');
const browsers = (process.argv[4] || 'chromium').split(',');
const ready = () => { const T = window.__krnT; return !!(T && T.paintDcl && (!T.diagStart || T.diagDone)); };

(async () => {
  for (const bname of browsers) {
    const browser = await ({ chromium, firefox, webkit })[bname].launch({ headless: true });
    for (const variant of variants) {
      const context = await browser.newContext({ viewport: { width: 1400, height: 900 } });
      const page = await context.newPage();
      const errors = [];
      page.on('pageerror', e => errors.push(String(e.message).slice(0, 160)));
      const base = pathToFileURL(path.join(dir, variant + '.html')).href;
      await page.goto(base, { waitUntil: 'commit' });
      await page.waitForFunction(ready, null, { polling: 200, timeout: 180000 });
      // Pick a scenario anchor far down the page for the deep link
      const anchor = await page.evaluate(() => {
        const ids = [...document.querySelectorAll('[id^="sid-"]')].map(e => e.id);
        return ids[Math.floor(ids.length * 0.6)] || null;
      });
      // 1. Deep link: a fresh load with the hash
      const page2 = await context.newPage();
      page2.on('pageerror', e => errors.push(String(e.message).slice(0, 160)));
      await page2.goto(base + '#' + anchor, { waitUntil: 'commit' });
      await page2.waitForFunction(ready, null, { polling: 200, timeout: 180000 });
      await page2.waitForTimeout(1500);
      const deep = await page2.evaluate(a => {
        const el = document.getElementById(a);
        const r = el ? el.getBoundingClientRect() : null;
        return { hash: location.hash === '#' + a, scrollY: Math.round(scrollY), inView: !!r && r.top > -50 && r.top < innerHeight };
      }, anchor);
      // 2. Back after an in-page link: same document, hash restored, no new decode
      const nav = await page.evaluate(async a => {
        const before = location.hash;
        const T = window.__krnT, writtenBefore = T.written;
        location.hash = '#' + a;
        await new Promise(r => setTimeout(r, 400));
        history.back();
        await new Promise(r => setTimeout(r, 800));
        return { backHash: location.hash === before, sameDoc: window.__krnT && window.__krnT.written === writtenBefore };
      }, anchor);
      // 3. A diagram renders through the page's own pipeline (engine from the CDN)
      await page.evaluate(() => {
        const el = document.querySelector('.plantuml-browser[id]');
        let d = el; while (d) { if (d.tagName === 'DETAILS') d.open = true; d = d.parentElement; }
        if (el) el.scrollIntoView();
      });
      let svg = false;
      try {
        await page.waitForFunction(() => { const el = document.querySelector('.plantuml-browser[id]'); return !!(el && el.querySelector('svg')); },
          null, { polling: 200, timeout: 60000 });
        svg = true;
      } catch (e) { svg = false; }
      // 4. Search index state
      const search = await page.evaluate(() => (window.__kronikolSearch || {}).indexState || 'absent');
      console.log(`${bname}\t${variant}\tdeep-link hash=${deep.hash} inView=${deep.inView} scrollY=${deep.scrollY}\t` +
        `back hash=${nav.backHash} sameDoc=${nav.sameDoc}\tdiagram svg=${svg}\tsearch=${search}\terrors=${errors.length}${errors.length ? ' ' + errors[0] : ''}`);
      await context.close();
    }
    await browser.close();
  }
})();
