const L = require('./lib.js');
const { seq } = L;
(async () => {
  await L.start();
  await seq('S1 search, close, Clear All, same search again', 'sep', async (p, snap, say) => {
    await L.openOutline(p);
    await snap('start');
    await L.type(p, 'zebra');
    await snap('search zebra');
    await L.toggleSection(p, 'details.scenario-background');
    await snap('reader closes Background');
    await p.locator('button', { hasText: 'Clear All' }).first().click();
    await p.waitForTimeout(100);
    await snap('Clear All');
    say('bar value=' + JSON.stringify(await p.inputValue('#searchbar')));
    await L.typeOver(p, 'zebra');
    await snap('search zebra again');
  });
  await seq('S1b search, close, empty via bar, same search again', 'sep', async (p, snap) => {
    await L.openOutline(p);
    await L.type(p, 'zebra');
    await L.toggleSection(p, 'details.scenario-background');
    await snap('reader closes Background');
    await L.empty(p);
    await snap('emptied');
    await L.type(p, 'zebra');
    await snap('search zebra again');
  });
  await seq('S2 search, close, Back, same search again', 'sep', async (p, snap, say) => {
    await L.openOutline(p);
    await L.type(p, 'zebra');
    await L.toggleSection(p, 'details.scenario-background');
    await snap('reader closes Background');
    say('hash=' + await p.evaluate(() => location.hash));
    await p.goBack();
    await p.waitForTimeout(300);
    say('after back hash=' + await p.evaluate(() => location.hash) + ' bar=' + JSON.stringify(await p.inputValue('#searchbar')));
    await snap('Back');
    await L.typeOver(p, 'zebra');
    await snap('search zebra again');
  });
  await L.stop();
})().catch(e => { console.error(e); process.exit(1); });
