const L = require('./lib.js');
const { seq } = L;
const panels = (p) => p.evaluate(() => {
  const g = [...document.querySelectorAll('details.scenario-parameterized')].find(x => x.querySelector('summary').textContent.includes('Pay'));
  return [...g.querySelectorAll('.param-detail-panel')].map(pn => { const b = pn.querySelector('details.scenario-background'), s = pn.querySelector('details.scenario-steps'); return 'b' + (b ? (b.open ? 'O' : 'c') : '-') + 's' + (s ? (s.open ? 'O' : 'c') : '-'); }).join(' ');
});
(async () => {
  await L.start();
  // The_search_changes_no_row_but_the_one_it_chose, as written, on this page: row f stands in for the fact's reader's row.
  await seq('V1 reader on f closes Steps; zebra picks d: what changed elsewhere', 'sep', async (p, snap, say) => {
    await L.openOutline(p);
    await L.row(p, 5);
    await L.toggleSection(p, 'details.scenario-steps');
    say('before ' + await panels(p));
    await L.type(p, 'zebra');
    say('after  ' + await panels(p));
  });
  // Reader closes Steps on a; search single-match picks e and empties; row left must read closed; composite kept.
  await seq('V2 a kept state past an emptied several-match search, then a hand switch', 'sep', async (p, snap) => {
    await L.openOutline(p);
    await L.toggleSection(p, 'details.scenario-steps');
    await L.type(p, 'okapi');
    await L.empty(p);
    await snap('emptied');
    await L.row(p, 5);
    await snap('reader clicks f');
  });
  await L.stop();
})().catch(e => { console.error(e); process.exit(1); });
