const L = require('./lib.js');
const { seq } = L;
const diag = (p) => p.evaluate(() => {
  const g = [...document.querySelectorAll('details.scenario-parameterized')].find(x => x.querySelector('summary').textContent.includes('Pay'));
  const d = g.querySelector('details.example-diagrams');
  return 'diagrams=' + (d ? d.open : 'none');
});
const plainDiag = (p, w) => p.evaluate((w) => {
  const s = [...document.querySelectorAll('details.scenario')].find(e => e.querySelector('summary').textContent.includes(w));
  const q = (c) => { const d = s.querySelector(c); return d ? (d.open ? 'O' : 'c') : '-'; };
  return `scenario=${q(':scope')} steps=${q('details.scenario-steps')} bg=${q('details.scenario-background')} diag=${q('details.example-diagrams')} shown=${s.style.display !== 'none'}`;
}, w);
(async () => {
  await L.start();
  await seq('T1 title-only single match: Refund', 'sep', async (p, snap) => {
    await L.openOutline(p, 'Refund');
    await L.toggleSection(p, 'details.scenario-background', 'Refund');
    await snap('reader opens Bg', 'Refund');
    await L.type(p, 'refund');
    await snap('refund', 'Refund');
    await L.toggleSection(p, 'details.scenario-steps', 'Refund');
    await snap('reader closes Steps', 'Refund');
    await L.append(p, ' x');
    await L.empty(p);
    await snap('none then emptied', 'Refund');
  });
  await seq('T2 Steps start closed; outline; search; several then single', 'sep-closed', async (p, snap) => {
    await L.openOutline(p);
    await snap('start');
    await L.type(p, 'okapi');
    await snap('okapi several -> e');
    await L.typeOver(p, 'stripe');
    await snap('stripe single -> e');
    await L.toggleSection(p, 'details.scenario-steps');
    await snap('reader closes Steps');
    await L.typeOver(p, 'okapi');
    await snap('okapi again (several)');
    await L.empty(p);
    await snap('emptied');
    await L.row(p, 4);
    await snap('reader clicks e');
  });
  await seq('T3 Background set to stay closed: failing row found by search', 'sep-bgfalse', async (p, snap) => {
    await L.openOutline(p);
    await snap('start');
    await L.row(p, 1);
    await snap('row b');
    await L.type(p, 'zebra');
    await snap('zebra');
    await L.empty(p);
    await snap('emptied');
  });
  await seq('T4 diagrams: search; reader closes Diagrams; Clear All; search again', 'diagrams', async (p, snap, say) => {
    await L.type(p, 'mango');
    say('mango            ' + await plainDiag(p, 'mango'));
    await p.locator('details.scenario', { hasText: 'mango' }).first().locator('details.example-diagrams > summary').first().click();
    await p.waitForTimeout(60);
    say('closes Diagrams  ' + await plainDiag(p, 'mango'));
    await L.append(p, ' customer');
    say('next word        ' + await plainDiag(p, 'mango'));
    await p.locator('button', { hasText: 'Clear All' }).first().click();
    await p.waitForTimeout(100);
    await L.typeOver(p, 'mango');
    say('Clear All, again ' + await plainDiag(p, 'mango'));
  });
  await seq('T5 diagrams report, outline: search picks row d: section and row', 'diagrams', async (p, snap, say) => {
    await L.openOutline(p);
    await snap('start');
    await L.type(p, 'zebra');
    await snap('zebra');
    say(await diag(p));
  });
  await seq('T6 typed one key at a time, reader closes between keys', 'sep', async (p, snap, say) => {
    await L.openFilters(p);
    const f = p.locator('#filtering-box #searchbar');
    await f.click();
    for (const ch of 'mang') { await p.keyboard.press(ch); await p.waitForTimeout(350); }
    say('mang          ' + await plainDiag(p, 'mango'));
    await p.locator('details.scenario', { hasText: 'mango' }).first().locator('details.scenario-steps > summary').first().click();
    await p.waitForTimeout(60);
    say('closes Steps  ' + await plainDiag(p, 'mango'));
    await f.click();
    await p.keyboard.press('o');
    await p.waitForTimeout(400);
    say('mango         ' + await plainDiag(p, 'mango'));
    for (let i = 0; i < 5; i++) { await p.keyboard.press('Backspace'); await p.waitForTimeout(60); }
    await p.waitForTimeout(400);
    say('deleted       ' + await plainDiag(p, 'mango') + ' bar=' + JSON.stringify(await p.inputValue('#searchbar')));
    for (const ch of 'mango') { await p.keyboard.press(ch); await p.waitForTimeout(350); }
    say('mango again   ' + await plainDiag(p, 'mango'));
  });
  await L.stop();
})().catch(e => { console.error(e); process.exit(1); });
