const L = require('./lib.js');
const { seq } = L;
(async () => {
  await L.start();
  await seq('S3 reader closes Steps on a; search picks e; reader clicks f while searching; empties; back to a', 'sep', async (p, snap) => {
    await L.openOutline(p);
    await L.toggleSection(p, 'details.scenario-steps');
    await snap('reader closes Steps on a');
    await L.type(p, 'stripe');
    await snap('search stripe -> e');
    await L.row(p, 5);
    await snap('reader clicks row f');
    await L.empty(p);
    await snap('emptied');
    await L.row(p, 0);
    await snap('reader clicks a');
  });
  await seq('S3b reader closes Steps on a; search picks e; reader clicks a again while searching', 'sep', async (p, snap) => {
    await L.openOutline(p);
    await L.toggleSection(p, 'details.scenario-steps');
    await snap('reader closes Steps on a');
    await L.type(p, 'stripe');
    await snap('search stripe -> e');
    await L.row(p, 0);
    await snap('reader clicks a');
    await L.empty(p);
    await snap('emptied');
  });
  await seq('S3c reader OPENS Background on a; several-match search picks d; reader clicks f', 'sep', async (p, snap) => {
    await L.openOutline(p);
    await L.toggleSection(p, 'details.scenario-background');
    await snap('reader opens Bg on a');
    await L.type(p, 'zebra');
    await snap('zebra -> d');
    await L.toggleSection(p, 'details.scenario-background');
    await snap('reader closes Bg on d');
    await L.row(p, 5);
    await snap('reader clicks f');
  });
  await seq('S3d reader opens composite on a; search picks e; reader clicks f', 'combined', async (p, snap) => {
    await L.openOutline(p);
    await p.locator('details.scenario-parameterized', { hasText: 'Pay' }).first().locator('.param-detail-panel:not([style*="display"]) details.step-collapsible > summary').first().click();
    await p.waitForTimeout(50);
    await snap('reader opens composite on a');
    await L.type(p, 'stripe');
    await snap('search stripe -> e');
    await L.row(p, 5);
    await snap('reader clicks f');
  });
  await seq('S4 several matches: reader closed Steps on a; search with several picks another row as it stands', 'sep', async (p, snap) => {
    await L.openOutline(p);
    await L.toggleSection(p, 'details.scenario-steps');
    await snap('reader closes Steps on a');
    await L.type(p, 'customer');
    await snap('customer (many)');
    await L.empty(p);
    await snap('emptied');
  });
  await seq('S5 search picks d; reader closes section; types on; empties; re-search with same field', 'sep', async (p, snap) => {
    await L.openOutline(p);
    await L.type(p, 'zebra');
    await snap('zebra');
    await L.toggleSection(p, 'details.scenario-steps');
    await snap('reader closes Steps on d');
    await L.append(p, ' customer');
    await snap('zebra customer');
    await L.empty(p);
    await snap('emptied');
    await L.row(p, 3);
    await snap('reader clicks d');
  });
  await seq('S6 search picks failing row by its message; closes; moves on', 'sep', async (p, snap) => {
    await L.openOutline(p);
    await L.row(p, 1);
    await snap('row b held');
    await L.toggleSection(p, 'details.scenario-background');
    await snap('reader closes held Bg');
    await L.row(p, 0);
    await snap('row a');
    await L.row(p, 1);
    await snap('row b again');
    await L.type(p, 'zebra');
    await snap('zebra');
    await L.empty(p);
    await snap('emptied back to b');
  });
  await L.stop();
})().catch(e => { console.error(e); process.exit(1); });
