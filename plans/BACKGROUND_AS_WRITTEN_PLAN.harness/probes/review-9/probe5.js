const L = require('./lib.js');
const fs = require('fs');
const R = L.R;
const info = () => {
  const list = [...document.querySelectorAll('details.scenario-steps')].find(s => s.offsetParent !== null && s.querySelector(':scope > .step-background'));
  const step = list.querySelector(':scope > .step-background');
  const cs = getComputedStyle(step);
  const section = getComputedStyle(list);
  const bg = getComputedStyle(document.body).backgroundColor;
  return { accent: cs.borderLeftColor, width: cs.borderLeftWidth, sectionBorder: section.borderTopColor, body: bg,
    text: getComputedStyle(step.querySelector('.step-text')).color, theme: document.documentElement.getAttribute('data-theme'),
    scheme: matchMedia('(prefers-color-scheme: dark)').matches };
};
function contrast(a, b) {
  const p = s => s.match(/[\d.]+/g).slice(0, 3).map(Number);
  const lum = c => { const v = c.map(x => { x /= 255; return x <= 0.03928 ? x / 12.92 : Math.pow((x + 0.055) / 1.055, 2.4); }); return 0.2126 * v[0] + 0.7152 * v[1] + 0.0722 * v[2]; };
  const [l1, l2] = [lum(p(a)), lum(p(b))].sort((x, y) => y - x);
  return ((l1 + 0.05) / (l2 + 0.05)).toFixed(2);
}
(async () => {
  await L.start();
  fs.mkdirSync(R + '/shots', { recursive: true });
  // dark theme, in a plain scenario and in an outline's panel, composite open
  for (const b of ['old', 'new']) {
    for (const scheme of ['light', 'dark']) {
      const p = await L.open(b, 'combined', { colorScheme: scheme, deviceScaleFactor: 3 });
      await p.evaluate(() => document.querySelectorAll('details.feature, details.scenario').forEach(d => d.setAttribute('open', '')));
      await p.evaluate(() => document.querySelectorAll('details.step-collapsible').forEach(d => d.setAttribute('open', '')));
      await p.waitForTimeout(200);
      const m = await p.evaluate(info);
      console.log(b, scheme, JSON.stringify(m), 'accent/body contrast', contrast(m.accent, m.body));
      const el = p.locator('details.scenario-parameterized', { hasText: 'Pay' }).first().locator('.param-detail-panel:not([style*="display"]) details.scenario-steps');
      await el.scrollIntoViewIfNeeded();
      const box = await el.boundingBox();
      await p.screenshot({ path: `${R}/shots/panel-${scheme}-${b}.png`, clip: { x: box.x, y: box.y, width: 420, height: Math.min(box.height, 260) } });
      await p.context().close();
    }
  }
  // exported copy: its accent, its row switch, its search
  for (const b of ['old', 'new']) {
    const p = await L.open(b, 'sep');
    await L.openFilters(p);
    const [dl] = await Promise.all([
      p.waitForEvent('download', { timeout: 15000 }),
      p.locator('button.export-btn', { hasText: 'Export Filtered HTML' }).first().click(),
    ]);
    const file = `${R}/out/${b}-export-sep.html`;
    await dl.saveAs(file);
    console.log(b, 'exported', fs.statSync(file).size);
    await p.context().close();

    const p2 = await L.open(b, 'combined');
    await L.type(p2, 'mango');
    await L.openFilters(p2);
    const [dl2] = await Promise.all([
      p2.waitForEvent('download', { timeout: 15000 }),
      p2.locator('button.export-btn', { hasText: 'Export Filtered HTML' }).first().click(),
    ]);
    const file2 = `${R}/out/${b}-export-combined.html`;
    await dl2.saveAs(file2);
    console.log(b, 'exported filtered', fs.statSync(file2).size);
    await p2.context().close();

    const ctx = await p.context().browser().newContext({ viewport: { width: 1280, height: 900 }, deviceScaleFactor: 2 });
    const q = await ctx.newPage();
    q.on('pageerror', e => console.log('  PAGEERROR export', b, e.message));
    await q.goto('file:///' + file2.replace(/\\/g, '/'));
    await q.waitForTimeout(500);
    const m = await q.evaluate(() => {
      const s = document.querySelector('details.scenario-steps > .step-background');
      if (!s) return { none: true, scenarios: document.querySelectorAll('details.scenario').length };
      const cs = getComputedStyle(s);
      return { scenarios: document.querySelectorAll('details.scenario').length, border: cs.borderLeftWidth + ' ' + cs.borderLeftStyle + ' ' + cs.borderLeftColor, ml: cs.marginLeft, pl: cs.paddingLeft };
    });
    console.log(b, 'exported page', JSON.stringify(m));
    await q.context().close();

    // the exported outline page: row switch and search run, held section marked
    const ctx3 = await p.context().browser().newContext({ viewport: { width: 1280, height: 900 } });
    const r = await ctx3.newPage();
    r.on('pageerror', e => console.log('  PAGEERROR export3', b, e.message));
    await r.goto('file:///' + file.replace(/\\/g, '/'));
    await r.locator('details.feature').first().waitFor();
    await L.openOutline(r);
    console.log(b, 'export start ', L.fmt(await L.st(r)));
    await L.row(r, 1);
    console.log(b, 'export row 1 ', L.fmt(await L.st(r)));
    await L.row(r, 3);
    console.log(b, 'export row 3 ', L.fmt(await L.st(r)));
    console.log(b, 'held attr count', await r.evaluate(() => document.querySelectorAll('[data-holds-failure]').length));
    await r.context().close();
  }
  await L.stop();
})().catch(e => { console.error(e); process.exit(1); });
