const L = require('./lib.js');
const path = require('path');
(async () => {
  const browser = await L.start();
  const ctx = await browser.newContext();
  const p = await ctx.newPage();
  await p.goto('file:///' + path.join(L.R, 'hook-floor/bin/Debug/net10.0/Reports/TestRunReport.html').replace(/\\/g, '/'));
  await p.locator('details.feature').first().waitFor();
  const out = await p.evaluate(() => [...document.querySelectorAll('details.scenario')].map(s => {
    const t = s.querySelector(':scope > summary').textContent.replace(/\s+/g, ' ').trim().slice(0, 30);
    const lists = [...s.querySelectorAll('details.scenario-steps')];
    return t + ' => ' + lists.map(l => [...l.querySelectorAll(':scope > .step')].map(x => (x.classList.contains('step-background') ? 'B' : 'o') + ((x.querySelector('.step-status') || {}).className || '').replace('step-status', '').trim()[0]).join('')).join(' / ');
  }));
  console.log(out.join('\n'));
  await L.stop();
})().catch(e => { console.error(e); process.exit(1); });
