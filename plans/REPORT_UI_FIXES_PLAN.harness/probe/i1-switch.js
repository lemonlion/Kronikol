'use strict';
// I1: the candidates for the switch beside a row's ANY/ALL, put into the real report's filter rows so they are judged
// against the real chips. The Tags row shows a candidate in its two-way state with one chip on; the Dependencies row the
// same candidate in its three-way state, pressed, with one chip on and one left out (red). One picture per candidate at
// 1280 px (light and dark) and 390 px (light).
// usage: node probe/i1-switch.js <main.html> <outDir>
const path = require('path');
const { open, frames } = require('./common');

const CSS = `
.k-ways { display: inline-flex; align-items: center; justify-content: center; gap: 4px; margin-right: 2px; min-height: 28px; box-sizing: border-box;
  padding: 6px 7px; border: 1px solid var(--kron-control-border); border-radius: 6px; background: var(--kron-field);
  color: var(--kron-ink-quiet); font: 600 11px/16px var(--kron-font); letter-spacing: 0.03em; cursor: pointer; vertical-align: baseline; }
.k-ways.is-three { border-color: var(--kron-selected-edge); background: var(--kron-selected); color: var(--kron-selected-ink); }
.k-ways svg { display: block; }
.k-ways .on { fill: var(--kron-selected-edge); } .k-ways .out { fill: var(--kron-exclude-edge); } .k-ways .off { fill: none; stroke: currentColor; }
`;

// each candidate: [name, markup for two-way, markup for three-way]
const LINES = (n) => `<svg width="14" height="14" viewBox="0 0 14 14" aria-hidden="true">${(n === 2 ? [4.5, 9.5] : [3, 7, 11]).map((y) => `<line x1="2" x2="12" y1="${y}" y2="${y}" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/>`).join('')}</svg>`;
const PIPS = (n) => { const w = n === 2 ? 20 : 29; const cells = n === 2 ? ['on', 'off'] : ['on', 'out', 'off'];
  return `<svg width="${w}" height="10" viewBox="0 0 ${w} 10" aria-hidden="true">${cells.map((c, i) => `<rect class="${c}" x="${0.75 + i * 9.5}" y="0.75" width="8" height="8.5" rx="4.25" ${c === 'off' ? 'stroke-width="1.2"' : ''}/>`).join('')}</svg>`; };
const GLYPHS = (n) => `<span aria-hidden="true" style="font-weight:700;letter-spacing:.08em">${n === 2 ? '✓&#8202;○' : '✓&#8202;✕&#8202;○'}</span>`;
const CANDIDATES = [
  ['lines', LINES(2), LINES(3)],
  ['pips', PIPS(2), PIPS(3)],
  ['words', '2-WAY', '3-WAY'],
  ['glyphs', GLYPHS(2), GLYPHS(3)],
];

async function place(page, cand) {
  await page.evaluate(([css, name, two, three]) => {
    if (!document.getElementById('k-ways-css')) { const s = document.createElement('style'); s.id = 'k-ways-css'; s.textContent = css; document.head.appendChild(s); }
    document.querySelectorAll('.k-ways').forEach((b) => b.remove());
    const put = (sel, html, pressed, title) => {
      const mode = document.querySelector(sel);
      const b = document.createElement('button'); b.type = 'button'; b.className = 'k-ways'; b.innerHTML = html;
      // as section 4.1 C9 has it: the text is the name, no aria-pressed, no aria-label; three-way shown by colour only
      if (pressed) b.classList.add('is-three'); b.title = title;
      mode.insertAdjacentElement('afterend', b);
    };
    put('.tag-mode-toggle', two, false, 'Two-way: a click shows only the scenarios with this tag, another click shows them all again. Click to make the chips three-way, where a second click leaves these scenarios out (red).');
    put('.dep-mode-toggle', three, true, 'Three-way: a click shows only these scenarios, a second leaves them out (red), a third shows them all again. Click to make the chips two-way.');
    // the rows' chips: one on in Tags; one on and one left out in Dependencies
    const set = (b, s) => { const f = b.classList.contains('tag-toggle') ? ['tag-active', 'tag-exclude'] : ['dependency-active', 'dependency-exclude'];
      b.classList.toggle(f[0], s === 'in'); b.classList.toggle(f[1], s === 'out'); };
    document.querySelectorAll('.tag-toggle, .dependency-toggle').forEach((b) => set(b, 'off'));
    const tags = document.querySelectorAll('.filtering-box .tag-toggle:not([hidden])'), deps = document.querySelectorAll('.filtering-box .dependency-toggle');
    if (tags[1]) set(tags[1], 'in');
    if (deps[0]) set(deps[0], 'in');
    if (deps[1]) set(deps[1], 'out');
  }, [CSS, ...cand]);
}

(async () => {
  const [file, outDir] = process.argv.slice(2);
  for (const [width, theme] of [[1280, 'light'], [1280, 'dark'], [390, 'light']]) {
    const { browser, page } = await open(file, width, 900, theme);
    try {
      // open the filter rows: More filters on a desktop, the Filters line on a phone
      await page.evaluate((phone) => {
        const t = phone ? document.querySelector('.mobile-filter-toggle')
          : Array.from(document.querySelectorAll('button')).find((b) => /more filters/i.test(b.textContent));
        if (t) t.click();
      }, width <= 768);
      await frames(page, 6);
      for (const cand of CANDIDATES) {
        await place(page, cand);
        await frames(page, 3);
        const rows = page.locator('.filtering-box .tag-filters, .filtering-box .dependency-filters');
        const boxes = await rows.evaluateAll((els) => els.map((e) => e.getBoundingClientRect()).map((r) => ({ x: r.left, y: r.top, w: r.width, h: r.height })));
        if (!boxes.length) throw new Error('the filter rows are not open');
        const x = Math.max(0, Math.min(...boxes.map((b) => b.x)) - 8), y = Math.max(0, Math.min(...boxes.map((b) => b.y)) - 8);
        const right = Math.max(...boxes.map((b) => b.x + b.w)) + 8, bottom = Math.max(...boxes.map((b) => b.y + b.h)) + 8;
        await page.screenshot({ path: path.join(outDir, `i1-${cand[0]}-${width}-${theme}.png`), clip: { x, y, width: Math.min(right, width) - x, height: bottom - y } });
      }
    } finally {
      await browser.close();
    }
  }
  console.log('done');
})().catch((e) => { console.error(e); process.exit(1); });
