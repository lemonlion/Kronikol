const L = require('./lib.js');
const R = L.R;
const measure = () => {
  const out = {};
  const de = document.documentElement;
  out.page = { sw: de.scrollWidth, cw: de.clientWidth, overflow: de.scrollWidth > de.clientWidth };
  const groups = [...document.querySelectorAll('details.scenario')];
  out.rows = [];
  groups.forEach(g => {
    if (!g.open) return;
    const shownSteps = [...g.querySelectorAll('details.scenario-steps')].filter(s => s.offsetParent !== null);
    shownSteps.forEach(list => {
      const lr = list.getBoundingClientRect();
      [...list.querySelectorAll(':scope > .step')].forEach(s => {
        const r = s.getBoundingClientRect();
        const cs = getComputedStyle(s);
        const sm = s.querySelector(':scope > summary');
        const status = s.querySelector('.step-status');
        const before = sm ? getComputedStyle(sm, '::before') : null;
        const smr = sm ? sm.getBoundingClientRect() : null;
        out.rows.push({
          text: (s.querySelector('.step-text') || {}).textContent ? s.querySelector('.step-text').textContent.slice(0, 22) : '',
          bg: s.classList.contains('step-background'), comp: s.classList.contains('step-collapsible'),
          left: +(r.left - lr.left).toFixed(1), right: +(lr.right - r.right).toFixed(1),
          statusLeft: status ? +(status.getBoundingClientRect().left - lr.left).toFixed(1) : null,
          border: cs.borderLeftWidth + ' ' + cs.borderLeftStyle + ' ' + cs.borderLeftColor,
          ml: cs.marginLeft, pl: cs.paddingLeft, sw: s.scrollWidth, cw: s.clientWidth,
          chevron: sm ? { left: +(smr.left - lr.left + parseFloat(before.left)).toFixed(1), width: before.width } : null,
        });
      });
    });
  });
  return out;
};
(async () => {
  await L.start();
  const shots = [
    { name: 'desk', vp: { width: 1280, height: 900 } },
    { name: 'phone', vp: { width: 375, height: 800 } },
    { name: 'tiny', vp: { width: 320, height: 700 } },
  ];
  for (const report of ['combined', 'combined-num']) {
    for (const s of shots) {
      for (const b of ['old', 'new']) {
        const p = await L.open(b, report, { viewport: s.vp, deviceScaleFactor: 2 });
        await p.evaluate(() => {
          document.querySelectorAll('details.feature, details.scenario').forEach(d => d.setAttribute('open', ''));
        });
        await p.waitForTimeout(200);
        const m = await p.evaluate(measure);
        console.log(`\n--- ${report} ${s.name} ${b} page overflow=${m.page.overflow} sw=${m.page.sw} cw=${m.page.cw}`);
        const seen = new Set();
        m.rows.forEach(r => {
          const k = JSON.stringify(r);
          if (seen.has(k)) return; seen.add(k);
          console.log('  ' + JSON.stringify(r));
        });
        if (report === 'combined') {
          const el = p.locator('details.scenario', { hasText: 'Plain mango' }).first().locator('details.scenario-steps');
          await el.scrollIntoViewIfNeeded();
          await el.screenshot({ path: `${R}/shots/${report}-${s.name}-${b}.png` });
        }
        await p.context().close();
      }
    }
  }
  await L.stop();
})().catch(e => { console.error(e); process.exit(1); });
