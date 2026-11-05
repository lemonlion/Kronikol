const L = require('./lib.js');
const { seq } = L;
const R = L.R;
(async () => {
  await L.start();
  await seq('P6 several matches: reader closes Steps on a; okapi picks e; reader clicks f; then a', 'sep', async (p, snap) => {
    await L.openOutline(p);
    await L.toggleSection(p, 'details.scenario-steps');
    await snap('reader closes Steps on a');
    await L.type(p, 'okapi');
    await snap('okapi -> e (2 matches)');
    await L.row(p, 5);
    await snap('reader clicks f');
    await L.row(p, 0);
    await snap('reader clicks a');
  });
  await seq('P6b several matches: reader opens Background on a; okapi picks e; emptied; reader clicks f', 'sep', async (p, snap) => {
    await L.openOutline(p);
    await L.toggleSection(p, 'details.scenario-background');
    await snap('reader opens Bg on a');
    await L.type(p, 'okapi');
    await snap('okapi -> e');
    await L.empty(p);
    await snap('emptied');
    await L.row(p, 5);
    await snap('reader clicks f');
  });
  await seq('P6c several matches while searching: reader opens Bg on a, okapi picks e, reader clicks f, emptied', 'sep', async (p, snap) => {
    await L.openOutline(p);
    await L.toggleSection(p, 'details.scenario-background');
    await L.type(p, 'okapi');
    await snap('okapi -> e');
    await L.row(p, 5);
    await snap('reader clicks f');
    await L.row(p, 0);
    await snap('reader clicks a');
  });
  await seq('P11 Clear All: row marks and the chosen row', 'sep', async (p, snap, say) => {
    await L.openOutline(p);
    await L.type(p, 'zebra');
    await snap('zebra');
    say('marks ' + await p.evaluate(() => document.querySelectorAll('tr.row-search-match').length));
    await p.locator('button', { hasText: 'Clear All' }).first().click();
    await p.waitForTimeout(100);
    await snap('Clear All');
    say('marks ' + await p.evaluate(() => document.querySelectorAll('tr.row-search-match').length));
  });
  await seq('P18 no-match then the empty state Clear: mango, close Steps, mangox, Clear, mango', 'sep', async (p, snap, say) => {
    const s = () => p.evaluate(() => { const x = [...document.querySelectorAll('details.scenario')].find(e => e.querySelector('summary').textContent.includes('mango')); const q = (c) => { const d = x.querySelector(c); return d ? d.open : null; }; return 'scenario=' + x.open + ' steps=' + q('details.scenario-steps') + ' bg=' + q('details.scenario-background') + ' display=' + (x.style.display || 'shown'); });
    await L.type(p, 'mango');
    say('mango            ' + await s());
    await p.locator('details.scenario', { hasText: 'mango' }).first().locator('details.scenario-steps > summary').first().click();
    await p.waitForTimeout(50);
    say('reader closes Steps ' + await s());
    await L.append(p, 'x');
    say('mangox           ' + await s());
    const clear = p.locator('.filters-empty-clear');
    say('empty state shows ' + await clear.isVisible());
    await clear.click();
    await p.waitForTimeout(100);
    say('Clear in the note ' + await s());
    await L.typeOver(p, 'mango');
    say('mango again      ' + await s());
    say('"the mango customer pays" visible: ' + await p.getByText('the mango customer pays').first().isVisible());
  });
  await seq('P10 load with #q=zebra', 'sep', async (p, snap, say, b) => {
    await p.goto(L.url(b, 'sep') + '#q=zebra');
    await p.locator('details.feature').first().waitFor();
    await p.waitForTimeout(400);
    await snap('loaded');
    await L.empty(p);
    await snap('emptied');
  });
  // the triangle over the accent still toggles; a wide Background at phone width
  for (const b of ['old', 'new']) {
    const p = await L.open(b, 'combined', { viewport: { width: 320, height: 700 } });
    await p.evaluate(() => document.querySelectorAll('details.feature, details.scenario').forEach(d => d.setAttribute('open', '')));
    await p.waitForTimeout(200);
    const wide = await p.evaluate(() => {
      const s = [...document.querySelectorAll('details.scenario')].find(e => e.querySelector('summary').textContent.includes('wide'));
      const rows = [...s.querySelectorAll('details.scenario-steps > .step')];
      const de = document.documentElement;
      const list = s.querySelector('details.scenario-steps').getBoundingClientRect();
      return { pageOverflow: de.scrollWidth > de.clientWidth, sw: de.scrollWidth, rows: rows.map(r => ({ bg: r.classList.contains('step-background'), sw: r.scrollWidth, cw: r.clientWidth, right: +(list.right - r.getBoundingClientRect().right).toFixed(1) })) };
    });
    console.log(b, 'wide at 320', JSON.stringify(wide));
    await p.context().close();
    const q = await L.open(b, 'combined');
    await q.evaluate(() => document.querySelectorAll('details.feature, details.scenario').forEach(d => d.setAttribute('open', '')));
    await q.waitForTimeout(200);
    const res = await q.evaluate(() => {
      const s = [...document.querySelectorAll('details.scenario')].find(e => e.querySelector('summary').textContent.includes('mango'));
      const d = s.querySelector('details.scenario-steps > .step-collapsible.step-background');
      d.scrollIntoView({ block: 'center' });
      const m = d.querySelector(':scope > summary').getBoundingClientRect();
      const before = getComputedStyle(d.querySelector(':scope > summary'), '::before');
      return { x: m.left + parseFloat(before.left) + 4, y: m.top + parseFloat(before.top) + 4 };
    });
    await q.mouse.click(res.x, res.y);
    await q.waitForTimeout(80);
    console.log(b, 'click on the triangle at', JSON.stringify(res), 'opens it:', await q.evaluate(() => {
      const s = [...document.querySelectorAll('details.scenario')].find(e => e.querySelector('summary').textContent.includes('mango'));
      return s.querySelector('details.scenario-steps > .step-collapsible.step-background').open;
    }));
    await q.context().close();
  }
  await L.stop();
})().catch(e => { console.error(e); process.exit(1); });
