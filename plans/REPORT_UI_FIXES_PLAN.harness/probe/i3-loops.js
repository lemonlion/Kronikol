'use strict';
// I3: what a hide toggle leaves of the loops and the Setup box. For each toggle state, the drawn source's block lines
// (`loop `, `partition`, `end`), whether each block holds anything between its opener and its `end`, the loop and Setup
// boxes the picture draws (by the 5.8.0 look's fills), and a picture of the diagram.
// usage: node probe/i3-loops.js <main.html> <outDir>
const path = require('path');
const { open, frames, openScenario, write } = require('./common');

const STATES = [
  ['shown', []],
  ['databases-off', ['databases']],
  ['events-off', ['events']],
  ['databases-steps-off', ['databases', 'steps']],
];

(async () => {
  const [file, outDir] = process.argv.slice(2);
  const { browser, page } = await open(file, 1600, 1000, 'light');
  const results = [];
  try {
    await openScenario(page, 'Collect milk');
    let on = new Set();
    for (const [name, off] of STATES) {
      // flip each report toggle whose state differs from the one wanted
      for (const t of ['databases', 'events', 'steps']) {
        const want = off.includes(t);
        if (want !== on.has(t)) {
          const before = await page.evaluate(() => document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"]').getAttribute('data-plantuml'));
          await page.evaluate((t) => document.querySelector('#display-settings [data-toggle="' + t + '"]').click(), t);
          await page.waitForFunction((before) => {
            const c = document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"]');
            return c.getAttribute('data-plantuml') !== before && !c._noteRendering && !window._plantumlRendering && c.querySelector('svg[data-k-roles]');
          }, before, { polling: 200, timeout: 60000 });
          if (want) on.add(t); else on.delete(t);
        }
      }
      await frames(page, 10);
      const r = await page.evaluate(() => {
        const c = document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"]');
        const lines = c.getAttribute('data-plantuml').split('\n').map((l) => l.trim());
        // a block is empty when nothing but blank lines and comments stands between its opener and its end
        const blocks = []; const stack = [];
        let inNote = false;
        // the render script's own note rules (plantuml-browser-render-script.js, _noteStatementRx and opensNoteBlock)
        const noteRx = /^[hr]?note\b(?:\s*<<[^>]*>>)*\s*(?:left|right|over|across)\b/i;
        const opensNote = (t) => { if (!noteRx.test(t)) return false; const h = t.replace(/<<[^>]*>>/g, ''); const c = h.indexOf(':'), a = h.indexOf('<'); return !(c >= 0 && (a < 0 || c < a)); };
        const mark = () => { if (stack.length) stack[stack.length - 1].content = true; };
        for (const t of lines) {
          if (inNote) { if (/^end\s*[hr]?note$/i.test(t)) inNote = false; continue; }
          if (opensNote(t)) { inNote = true; mark(); continue; }
          if (/^(loop |partition\b)/.test(t)) { stack.push({ opener: t, content: false }); continue; }
          if (t === 'end' && stack.length) { const b = stack.pop(); blocks.push(b); if (b.content) mark(); continue; }
          if (t && !t.startsWith("'")) mark();
        }
        const svg = c.querySelector('svg');
        return {
          loops: lines.filter((l) => l.startsWith('loop ')).length,
          partitions: lines.filter((l) => /^partition\b/.test(l)).length,
          emptyBlocks: blocks.filter((b) => !b.content).map((b) => b.opener),
          loopBoxesDrawn: svg.querySelectorAll('rect[fill="#F4F7FB" i]').length,
          svgHeight: Math.round(svg.getBoundingClientRect().height)
        };
      });
      r.state = name;
      results.push(r);
      await page.locator('details.scenario[data-probe] [data-diagram-type="plantuml"]').first().screenshot({ path: path.join(outDir, `i3-${name}.png`) });
      console.log(JSON.stringify(r));
    }
    write(path.join(outDir, 'i3-loops.json'), results);
  } finally {
    await browser.close();
  }
})().catch((e) => { console.error(e); process.exit(1); });
