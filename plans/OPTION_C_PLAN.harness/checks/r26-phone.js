// Item 26 at phone width: the gear on the diagram's corner is hidden, the phone's own settings button on the title
// line still opens the bar, which starts "This scenario:".
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const path = require('path');
(async () => {
  const [page, shot] = process.argv.slice(2);
  const b = await pw.chromium.launch({ channel: 'chrome' });
  const p = await (await b.newContext({ viewport: { width: 400, height: 800 }, isMobile: true, hasTouch: true })).newPage();
  const errors = []; p.on('pageerror', e => errors.push(String(e)));
  await p.goto('file:///' + path.resolve(page).split(path.sep).join('/')); await p.waitForTimeout(2500);
  await p.evaluate(() => {
    const s = Array.from(document.querySelectorAll('details.scenario')).find(x => x.querySelector(':scope > summary').textContent.includes('A valid apple cinnamon muffin request should return a fresh batch'));
    let a = s; while (a) { if (a.tagName === 'DETAILS') a.open = true; a = a.parentElement; }
    const d = s.querySelector('details.example-diagrams'); d.open = true; d.setAttribute('data-probe', '1');
  });
  await p.waitForTimeout(2000);
  const st = () => p.evaluate(() => {
    const d = document.querySelector('[data-probe]');
    const g = d.querySelector('.k-scn-gear'), pg = d.querySelector('summary .gear-in-summary'), bar = d.querySelector('.k-scn-settings > .diagram-toggle');
    return { kGear: g.getBoundingClientRect().width, phoneGear: pg ? pg.getBoundingClientRect().width : -1, bar: bar.getBoundingClientRect().height, first: bar.firstElementChild.textContent, wrapDisplay: getComputedStyle(d.querySelector('.k-scn-settings')).display };
  });
  const before = await st();
  await p.evaluate(() => document.querySelector('[data-probe] summary .gear-in-summary').scrollIntoView({ block: 'center' }));
  await p.waitForTimeout(300);
  await p.locator('[data-probe] summary .gear-in-summary').tap();
  await p.waitForTimeout(500);
  const after = await st();
  console.log('before', JSON.stringify(before)); console.log('after ', JSON.stringify(after));
  if (shot) await p.screenshot({ path: shot });
  const ok = before.kGear === 0 && before.phoneGear > 0 && before.bar === 0 && after.bar > 0 && after.first === 'This scenario:' && errors.length === 0;
  console.log(ok ? 'PASS' : 'FAIL ' + errors.join(' | '));
  await b.close();
})();
