const L = require('./lib.js');
const { seq } = L;
const R = L.R;
const D = 'Deferred';
const comps = (p) => p.evaluate(() => {
  const g = [...document.querySelectorAll('details.scenario-parameterized')].find(x => x.querySelector('summary').textContent.includes('Deferred'));
  return [...g.querySelectorAll('.param-detail-panel')].map(panel => {
    const top = panel.querySelector('details.scenario-steps > details.step-collapsible');
    const sub = top.querySelector(':scope > .sub-steps');
    const nested = sub.querySelector(':scope > details.step-collapsible');
    return (panel.style.display === 'none' ? ' ' : '*') + (top.open ? 'O' : 'c') + ' held=' + sub.hasAttribute('data-steps') + ' subs=' + sub.children.length + ' nested=' + (nested ? (nested.open ? 'O' : 'c') : '-');
  }).join(' | ');
});
const open = async (p, sel) => {
  await p.locator('details.scenario-parameterized', { hasText: D }).first().locator('.param-detail-panel:not([style*="display"]) ' + sel).first().click();
  await p.waitForTimeout(120);
};
const TOP = 'details.scenario-steps > details.step-collapsible > summary';
const NESTED = 'details.scenario-steps > details.step-collapsible > .sub-steps > details.step-collapsible > summary';
(async () => {
  await L.start();
  await seq('D1 deferred sub-steps: open composite and nested on u, switch to v and w, close, return', 'combined', async (p, snap, say) => {
    await L.openOutline(p, D);
    say('start        ' + await comps(p));
    await open(p, TOP);
    await open(p, NESTED);
    say('u: both open ' + await comps(p));
    await L.row(p, 1, D);
    await p.waitForTimeout(200);
    say('v            ' + await comps(p));
    await open(p, NESTED);
    say('v: nested c  ' + await comps(p));
    await open(p, TOP);
    say('v: top c     ' + await comps(p));
    await L.row(p, 2, D);
    await p.waitForTimeout(200);
    say('w            ' + await comps(p));
    await open(p, TOP);
    await p.waitForTimeout(200);
    say('w: top O     ' + await comps(p));
    await L.row(p, 0, D);
    await p.waitForTimeout(200);
    say('u again      ' + await comps(p));
  });
  // BrowserJs: the row's diagram when the section opens after the row is chosen
  for (const b of ['old', 'new']) {
    const p = await L.open(b, 'browserjs');
    const errs = [];
    p.on('requestfailed', r => errs.push(r.url().slice(0, 80)));
    await L.openOutline(p);
    await L.type(p, 'zebra');
    const snap = async () => p.evaluate(() => {
      const g = [...document.querySelectorAll('details.scenario-parameterized')].find(x => x.querySelector('summary').textContent.includes('Pay'));
      const sec = g.querySelector('details.example-diagrams');
      const active = g.querySelector('tbody tr.row-active').getAttribute('data-row-idx');
      const d = [...g.querySelectorAll('[id*="-diagram-"]')].filter(x => /-diagram-\d+$/.test(x.id)).map(x => {
        const el = x.querySelector('.plantuml-browser');
        return x.id.replace(/.*-diagram-/, '') + (x.style.display === 'none' ? 'h' : 'S') + ':' + (el ? ((el.dataset.queued ? 'q' : '') + (el.hasAttribute('data-rendered') ? 'r' : '') + (el.querySelector('svg') ? 'svg' : '')) : 'none');
      });
      return { section: sec && sec.open, active, d: d.join(' ') };
    });
    console.log(b, 'just after search', JSON.stringify(await snap()));
    await p.waitForTimeout(6000);
    console.log(b, 'after 6s        ', JSON.stringify(await snap()), 'failed requests', errs.slice(0, 2));
    await p.context().close();
  }
  await L.stop();
})().catch(e => { console.error(e); process.exit(1); });
