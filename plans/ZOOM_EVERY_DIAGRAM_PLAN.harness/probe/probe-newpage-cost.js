'use strict';
// What would PlantUML's own `newpage` cost as the way to draw a long diagram in lined-up parts? For each scenario's
// diagram in parts: its parts drawn as today (sent together), the whole diagram drawn at once, and the whole diagram with
// a `newpage` before each part's first message, of which the browser build draws page 0 (it always draws page 0) after
// laying out every page. Times, widths, heights and text counts. Each drawing misses the cache (a comment line).
// usage: node probe-newpage-cost.js <report.html> <scenario,scenario,...> [whole: 1 to draw the whole diagram too]
const path = require('path');
const { chromium } = require(process.env.PLAYWRIGHT_CORE || 'playwright-core');
const { launchInstalled } = require(path.join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));

(async () => {
  const [file, list, wholeArg = '1'] = process.argv.slice(2);
  const wanted = list.split(',').map(Number);
  const browser = await launchInstalled(chromium, { headless: true });
  try {
    const page = await (await browser.newContext({ viewport: { width: 1920, height: 1000 } })).newPage();
    await page.goto('file:///' + path.resolve(file).split(path.sep).join('/'), { timeout: 600000 });
    await page.evaluate((wanted) => {
      const scs = document.querySelectorAll('details.scenario');
      wanted.forEach((si) => {
        const sc = scs[si]; let p = sc; while (p) { if (p.tagName === 'DETAILS') p.open = true; p = p.parentElement; }
        window._renderDiagramsInContainer(sc);
      });
    }, wanted);
    await page.waitForFunction((wanted) => {
      const scs = document.querySelectorAll('details.scenario');
      return wanted.every((si) => {
        const c = Array.from(scs[si].querySelectorAll('[data-diagram-type="plantuml"]')).find((x) => x.querySelectorAll(':scope > .puml-fragment').length > 1);
        return c && Array.from(c.querySelectorAll(':scope > .puml-fragment')).every((p) => p.querySelector(':scope > svg'));
      });
    }, wanted, { timeout: 600000, polling: 500 });
    await page.waitForTimeout(2000);
    const out = await page.evaluate(async ({ wanted, whole }) => {
      let seq = 0;
      function render(source) {
        return new Promise((resolve) => {
          const div = document.createElement('div');
          div.id = 'newpage-cost-' + (seq++);
          div.style.cssText = 'position:absolute;left:-300000px;top:0;visibility:hidden';
          document.body.appendChild(div);
          const t0 = performance.now();
          const mo = new MutationObserver(() => done());
          function done() {
            const svg = div.querySelector('svg');
            if (!svg && !div.textContent.trim()) return;
            mo.disconnect();
            const r = svg ? { ms: performance.now() - t0, w: svg.width.baseVal.value, h: svg.height.baseVal.value, texts: svg.querySelectorAll('text').length }
              : { ms: performance.now() - t0, error: div.textContent.trim().slice(0, 160) };
            div.remove();
            resolve(r);
          }
          mo.observe(div, { childList: true, subtree: true, characterData: true });
          window.plantuml.render(source.split('\n'), div.id);
          done();
        });
      }
      const bust = (src, tag) => src.replace(/^@startuml.*$/m, (m) => m + "\n' probe " + tag + ' ' + Math.random().toString(36).slice(2));
      const msgRe = /^\s*([\w.]+)\s*(<?[-.]*(?:\[[^\]]*\])?[-.]*>?>?)\s*([\w.]+)\s*(?::(.*))?$/;
      const isMsg = (l) => { const m = l.trim().match(msgRe); return !!(m && m[2].indexOf('-') >= 0 && !/^\s*note\b/.test(l)); };
      // messages outside note bodies, counted in order
      function messageLines(lines) {
        const at = []; let inNote = false;
        lines.forEach((l, i) => {
          const t = l.trim();
          if (inNote) { if (/^end\s*[rh]?note$/.test(t)) inNote = false; return; }
          if (/^[rh]?note\b/.test(t)) { if (!/:/.test(t)) inNote = true; return; }
          if (isMsg(l)) at.push(i);
        });
        return at;
      }
      const scs = document.querySelectorAll('details.scenario');
      const rows = [];
      for (const si of wanted) {
        const c = Array.from(scs[si].querySelectorAll('[data-diagram-type="plantuml"]')).find((x) => x.querySelectorAll(':scope > .puml-fragment').length > 1);
        const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment')).map((p) => p.getAttribute('data-plantuml'));
        const full = c._fullSource || c.getAttribute('data-plantuml');
        const lines = full.split('\n');
        const at = messageLines(lines);
        // a newpage before the first message of each part after the first
        const counts = parts.map((s) => messageLines(s.split('\n')).length);
        const cuts = new Set(); let k = 0;
        counts.slice(0, -1).forEach((n) => { k += n; if (at[k] !== undefined) cuts.add(at[k]); });
        const paged = lines.map((l, i) => (cuts.has(i) ? 'newpage\n' + l : l)).join('\n');
        const row = { si, parts: parts.length, messages: at.length, partMessages: counts };
        const t0 = performance.now();
        const ps = await Promise.all(parts.map((s) => render(bust(s, 'part'))));
        row.partsWall = performance.now() - t0;
        row.partsSum = ps.reduce((a, r) => a + r.ms, 0);
        row.partsWidest = Math.max(...ps.map((r) => r.w || 0));
        row.partsTexts = ps.reduce((a, r) => a + (r.texts || 0), 0);
        const np = await render(bust(paged, 'newpage'));
        row.newpage = np;
        if (whole) row.whole = await render(bust(full, 'whole'));
        rows.push(row);
      }
      return rows;
    }, { wanted, whole: wholeArg === '1' });
    for (const r of out) {
      console.log(`scenario ${r.si}: ${r.parts} parts, ${r.messages} messages (${r.partMessages.join('+')})`);
      console.log(`  parts as today: ${Math.round(r.partsWall)} ms wall, ${Math.round(r.partsSum)} ms summed, widest ${Math.round(r.partsWidest)} px, ${r.partsTexts} texts`);
      const d = (x) => (x.error ? 'ERROR ' + x.error : `${Math.round(x.ms)} ms, ${Math.round(x.w)} x ${Math.round(x.h)} px, ${x.texts} texts`);
      console.log(`  newpage, page 0 of ${r.parts}: ${d(r.newpage)}`);
      if (r.whole) console.log(`  whole at once: ${d(r.whole)}`);
    }
  } finally {
    await browser.close();
  }
})();
