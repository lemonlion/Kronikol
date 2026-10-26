'use strict';
// I6: each step bar of each variant scenario in stepbars.html (gen.cs): the label's centre and each table's centre
// against the bar's, every cell text's room to the rules around it, the header shading the role pass adds, and a picture
// of each bar beside the HTML step-param-table of the steps list. All in screen px at the drawing's scale (given).
// usage: node probe/i6-stepbars.js <stepbars.html> <outDir> [light|dark]
const path = require('path');
const { open, frames, openScenario, write } = require('./common');

function measureBars() {
  const box = document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"]');
  const svg = box.querySelector('svg');
  const r = (el) => { const b = el.getBoundingClientRect(); return { l: b.left, t: b.top, r: b.right, b: b.bottom, w: b.width, h: b.height }; };
  const scale = svg.getBoundingClientRect().width / (svg.width.baseVal.value || svg.getBoundingClientRect().width);
  const texts = Array.from(svg.querySelectorAll('text')).filter((t) => t.textContent.replace(/[\s​ ]/g, '') !== '').map((t) => ({ s: t.textContent, ...r(t) }));
  const rules = Array.from(svg.querySelectorAll('.k-step-rule')).map(r);
  const bars = Array.from(svg.querySelectorAll('.k-step')).map(r).sort((a, c) => a.t - c.t);
  const med = (xs) => { if (!xs.length) return null; const s = xs.slice().sort((a, c) => a - c); return +s[Math.floor(s.length / 2)].toFixed(1); };
  const min = (xs) => (xs.length ? +Math.min(...xs).toFixed(1) : null);
  return { scale: +scale.toFixed(3), bars: bars.map((bar) => {
    const inBar = (o) => o.t >= bar.t - 1 && o.b <= bar.b + 1 && o.l >= bar.l - 1 && o.r <= bar.r + 1;
    const bt = texts.filter(inBar);
    const firstY = Math.min(...bt.map((t) => t.t));
    const label = bt.filter((t) => Math.abs(t.t - firstY) < 2);
    const labelL = Math.min(...label.map((t) => t.l)), labelR = Math.max(...label.map((t) => t.r));
    const br = rules.filter(inBar);
    const hs = br.filter((o) => o.h <= 0.8), vs = br.filter((o) => o.w <= 0.8);
    // a table: the horizontal rules sharing one left and right end
    const keys = [...new Set(hs.map((h) => Math.round(h.l) + ':' + Math.round(h.r)))];
    const tables = keys.map((k) => {
      const [l, rr] = k.split(':').map(Number);
      const own = hs.filter((h) => Math.round(h.l) === l && Math.round(h.r) === rr).sort((a, c) => a.t - c.t);
      const top = own[0].t, bottom = own[own.length - 1].b;
      const cols = [...new Set(vs.filter((v) => v.t >= top - 1 && v.b <= bottom + 1 && v.l >= l - 1 && v.r <= rr + 1).map((v) => +v.l.toFixed(1)))].sort((a, c) => a - c);
      const cells = bt.filter((t) => t.t >= top && t.b <= bottom && t.l >= l && t.r <= rr);
      const pads = cells.map((t) => {
        const left = Math.max(...cols.filter((x) => x <= t.l + 0.5)), right = Math.min(...cols.filter((x) => x >= t.r - 0.5));
        const above = Math.max(...own.filter((h) => h.t <= t.t + 0.5).map((h) => h.b)), below = Math.min(...own.filter((h) => h.t >= t.b - 0.5).map((h) => h.t));
        return { left: t.l - left, right: right - t.r, top: t.t - above, bottom: below - t.b, centred: Math.abs((t.l + t.r) / 2 - (left + right) / 2) };
      });
      return { centreOffset: +(((l + rr) / 2) - (bar.l + bar.r) / 2).toFixed(1), width: rr - l, rows: own.length - 1, cols: cols.length - 1,
        padLeft: { min: min(pads.map((p) => p.left)), med: med(pads.map((p) => p.left)) },
        padRight: { min: min(pads.map((p) => p.right)), med: med(pads.map((p) => p.right)) },
        padTop: { min: min(pads.map((p) => p.top)), med: med(pads.map((p) => p.top)) },
        padBottom: { min: min(pads.map((p) => p.bottom)), med: med(pads.map((p) => p.bottom)) },
        cellOffCentre: { med: med(pads.map((p) => p.centred)) } };
    });
    const lines = bt.filter((t) => !tables.length || true).map((t) => +(((t.l + t.r) / 2) - (bar.l + bar.r) / 2).toFixed(1));
    return { label: label.map((t) => t.s).join(' ').slice(0, 60), barWidth: +bar.w.toFixed(1), barHeight: +bar.h.toFixed(1),
      labelCentreOffset: +(((labelL + labelR) / 2) - (bar.l + bar.r) / 2).toFixed(1), labelLeftFromBar: +(labelL - bar.l).toFixed(1),
      lineCentreOffsets: lines.slice(0, 12), tables,
      headerGrounds: Array.from(svg.querySelectorAll('.k-step-table-head')).filter((h) => inBar(r(h))).length,
      tableGrounds: Array.from(svg.querySelectorAll('.k-step-table')).filter((h) => inBar(r(h))).length };
  }) };
}

(async () => {
  const [file, outDir, theme] = process.argv.slice(2);
  const { browser, page } = await open(file, 1400, 1000, theme || 'light');
  const out = [];
  try {
    const names = await page.evaluate(() => Array.from(document.querySelectorAll('details.scenario summary')).map((s) => s.textContent).filter((t) => /Step bars \d/.test(t)));
    for (const full of names) {
      const name = full.match(/Step bars \d[^\n]*/)[0].trim().replace(/\s+\d+(\.\d+)?\s*m?s$/, '');
      const key = name.match(/Step bars (\d)/)[1];
      await openScenario(page, 'Step bars ' + key);
      await frames(page, 6);
      const m = await page.evaluate(measureBars);
      out.push({ variant: name, ...m });
      const loc = page.locator('details.scenario[data-probe] [data-diagram-type="plantuml"]').first();
      await loc.screenshot({ path: path.join(outDir, `i6-bars${key}-${theme || 'light'}.png`) });
      if (key === '0') {
        const t = page.locator('details.scenario[data-probe] .step-param-table').first();
        if (await t.count()) await t.screenshot({ path: path.join(outDir, `i6-step-param-table-${theme || 'light'}.png`) });
      }
    }
    write(path.join(outDir, `i6-stepbars-${theme || 'light'}.json`), out);
    console.log(JSON.stringify(out, null, 1));
  } finally {
    await browser.close();
  }
})().catch((e) => { console.error(e); process.exit(1); });
