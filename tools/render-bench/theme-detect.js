'use strict';
// Runs the REAL findNoteGroups/hasNoteFill/hasNoteFoldTriangle from collapsible-notes-script.js
// against each themed SVG and reports what the note detector sees.
const fs = require('fs'), path = require('path');
const PW = 'C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package';
const pw = require(PW);
const SRC = 'C:/Code/Kronikol/src/Kronikol/Reports/collapsible-notes-script.js';
const lines = fs.readFileSync(SRC, 'utf8').split('\n');
const fns = lines.slice(33, 186).join('\n') + '\n'
  + lines.slice(1778, 1828).join('\n') + '\n'
  + 'window.__findNoteGroups=findNoteGroups;window.__hasNoteFill=hasNoteFill;window.__hasFold=hasNoteFoldTriangle;window.__findAssert=findAssertionNoteGroups;';
const dir = path.join(__dirname, 'themeprobe', process.argv[2] || 'jar');
const svgs = fs.readdirSync(dir).filter(f => f.endsWith('.svg')).sort();

(async () => {
  const browser = await pw.chromium.launch({ headless: true });
  const page = await browser.newPage();
  await page.setContent('<html><body><div id="h"></div></body></html>');
  await page.evaluate((src) => { (0, eval)(src); }, fns);
  for (const f of svgs) {
    const svg = fs.readFileSync(path.join(dir, f), 'utf8');
    const r = await page.evaluate((svgText) => {
      const h = document.getElementById('h');
      h.innerHTML = svgText;
      const el = h.querySelector('svg');
      if (!el) return { err: 'no svg' };
      const mainG = Array.from(el.children).find(c => c.tagName === 'g');
      const topPaths = mainG ? Array.from(mainG.children).filter(c => c.tagName === 'path') : [];
      const fills = [...new Set(topPaths.map(p => (p.getAttribute('fill') || '(unset)').toLowerCase()))];
      const groups = window.__findNoteGroups(el);
      const folds = groups.filter(g => window.__hasFold(g.paths)).length;
      return {
        groups: groups.length,
        withFold: folds,
        fills: fills,
        assertGroups: (function(){var a=window.__findAssert(el);return a.map(function(g){return g.texts.map(function(t){return t.textContent;}).join('').slice(0,26);});})(),
        texts: groups.map(g => g.texts.map(t => t.textContent).join('~').replace(/\s+/g, ' ').slice(0, 60))
      };
    }, svg);
    const name = f.replace(/\.svg$/, '');
    if (r.err) { console.log(name.padEnd(19) + ' ERR ' + r.err); continue; }
    console.log(`${name.padEnd(19)} groups=${String(r.groups).padStart(2)} fold=${String(r.withFold).padStart(2)}  topPathFills=${JSON.stringify(r.fills)}`);
    (r.assertGroups||[]).length && console.log('      assert=' + JSON.stringify(r.assertGroups));
    r.texts.forEach((t, i) => console.log(`      [${i}] ${t}`));
  }
  await browser.close();
})();
