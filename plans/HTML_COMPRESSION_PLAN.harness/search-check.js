// Deep search in a compressed page against today's page: the same terms, found only inside diagram sources or flame
// spans, must show the same scenarios. The words themselves are never printed: d1..d3 and f1..f2 stand for them. Also re-runs the functional checks (deep link, Back, a diagram drawn).
// node search-check.js <file1.html,file2.html,...> [browsers csv]
const { chromium, firefox, webkit } = require('playwright-core');
const { pathToFileURL } = require('url');

const files = process.argv[2].split(',');
const browsers = (process.argv[3] || 'chromium').split(',');
const ready = () => { const T = window.__krnT; return !!(T && T.paintDcl); };

async function pickTerms(page) {
  return page.evaluate(async () => {
    const big = [];
    document.querySelectorAll('[data-search],[data-row-search]').forEach(e => {
      big.push(e.getAttribute('data-search') || '', e.getAttribute('data-row-search') || '');
    });
    big.push(document.body.textContent);
    const hay = big.join('\n').toLowerCase();
    const map = JSON.parse(document.getElementById('puml-data').textContent);
    const out = { diagram: [], flame: [] };
    const re = /[A-Za-z][A-Za-z0-9]{9,30}/g;
    // Words found in exactly one diagram and nowhere in the page's text, from diagrams at 10%, 50% and 90% of the
    // list (so that with pieces they sit in different pieces)
    const ids = Object.keys(map), sets = [], count = new Map();
    for (const id of ids) {
      const s = new Set(((await decompressGzipBase64(map[id])).match(re) || []));
      sets.push(s);
      for (const w of s) count.set(w, (count.get(w) || 0) + 1);
    }
    for (const at of [0.1, 0.5, 0.9]) {
      for (let i = Math.floor(ids.length * at); i < ids.length; i++) {
        const w = [...sets[i]].find(x => count.get(x) === 1 && !hay.includes(x.toLowerCase()));
        if (w) { out.diagram.push(w); out.diagramAt = (out.diagramAt || []).concat(i); break; }
      }
    }
    for (const f of [...document.querySelectorAll('.iflow-flame[data-flame-z]')].slice(0, 300)) {
      if (out.flame.length >= 2) break;
      const text = kronExtractFlameText(await decompressGzipBase64(f.getAttribute('data-flame-z')));
      const w = (text.match(re) || []).find(x => !hay.includes(x.toLowerCase()) && !out.flame.includes(x) && !out.diagram.includes(x));
      if (w) out.flame.push(w);
    }
    return out;
  });
}

async function search(page, term) {
  await page.evaluate(t => {
    const sb = document.getElementById('searchbar');
    sb.value = t; sb.dispatchEvent(new KeyboardEvent('keyup', { bubbles: true }));
  }, term);
  // Wait until the visible set holds still for 1.5 s (deep results arrive from the worker)
  let last = '', stableSince = Date.now(), t0 = Date.now();
  for (;;) {
    await page.waitForTimeout(250);
    const now = await page.evaluate(() => [...document.getElementsByClassName('scenario')]
      .filter(s => s.offsetParent !== null || getComputedStyle(s).display !== 'none').map(s => s.id).join(','));
    if (now !== last) { last = now; stableSince = Date.now(); }
    if (Date.now() - stableSince > 1500 || Date.now() - t0 > 30000) {
      const st = await page.evaluate(() => window.__kronikolSearch && window.__kronikolSearch.indexState);
      return { visible: last ? last.split(',').length : 0, ids: last, state: st, ms: Date.now() - t0 };
    }
  }
}

(async () => {
  for (const bname of browsers) {
    const browser = await ({ chromium, firefox, webkit })[bname].launch({ headless: true });
    let reference = null;
    for (const file of files) {
      const context = await browser.newContext({ viewport: { width: 1400, height: 900 } });
      const page = await context.newPage();
      const errors = [];
      page.on('pageerror', e => errors.push(String(e.message).slice(0, 160)));
      const url = pathToFileURL(file).href;
      await page.goto(url, { waitUntil: 'commit' });
      await page.waitForFunction(ready, null, { polling: 200, timeout: 180000 });
      const terms = await pickTerms(page);
      const name = file.split(/[\\/]/).pop();
      const results = {};
      for (const t of [...terms.diagram, ...terms.flame]) results[t] = await search(page, t);
      // Back and a deep link, as functional.js does
      const anchor = await page.evaluate(() => { const ids = [...document.querySelectorAll('[id^="sid-"]')].map(e => e.id); return ids[Math.floor(ids.length * 0.6)]; });
      const nav = await page.evaluate(async a => {
        const before = location.hash, w = window.__krnT.written;
        location.hash = '#' + a; await new Promise(r => setTimeout(r, 400));
        history.back(); await new Promise(r => setTimeout(r, 800));
        return { back: location.hash === before, sameDoc: window.__krnT.written === w };
      }, anchor);
      const m = await page.evaluate(() => window.__krnM || null);
      const label = t => (terms.diagram.includes(t) ? 'd' + (terms.diagram.indexOf(t) + 1) : 'f' + (terms.flame.indexOf(t) + 1));
      const summary = Object.entries(results).map(([t, r]) => `${label(t)}:${r.visible}/${r.state}/${r.ms}ms`).join(' ');
      let same = '';
      if (!reference) reference = { terms, results };
      else {
        const sameTerms = JSON.stringify(terms) === JSON.stringify(reference.terms);
        const sameSets = Object.keys(reference.results).every(t => results[t] && results[t].ids === reference.results[t].ids);
        same = `sameTerms=${sameTerms} sameResults=${sameSets}`;
      }
      console.log(`${bname}\t${name}\tterms d=${terms.diagram.length} f=${terms.flame.length}\t${summary}\tback=${nav.back} sameDoc=${nav.sameDoc}\t${same}\tpieces=${m ? m.pieceDecodes + '/' + m.pieceHeld : '-'}\terrors=${errors.length}${errors.length ? ' ' + errors[0] : ''}`);
      await context.close();
    }
    await browser.close();
  }
})().catch(e => { console.error(e); process.exit(1); });
