// The scenario toolbar's shape at 1400 px: its height and how many rows its controls occupy,
// with and without flow tracking, before (out) and after (out-patched) the prototype.
const pw = require(require('path').resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const path = require('path');
(async () => {
  const b = await pw.chromium.launch();
  for (const dir of ['out', 'out-patched']) for (const name of ['TestRunReport.html', 'TestRunReport_iflow.html']) {
    const p = await b.newPage({ viewport: { width: 1400, height: 900 } });
    await p.route('**/*', r => r.request().url().startsWith('file:') ? r.continue() : r.abort());
    await p.goto('file:///' + path.join(__dirname, dir, name).replace(/\\/g, '/'), { waitUntil: 'domcontentloaded' });
    await p.waitForSelector('details.feature');
    await p.evaluate(() => document.querySelectorAll('details').forEach(d => d.open = true));
    await p.waitForTimeout(150);
    const r = await p.evaluate(() => {
      const t = document.querySelector('.diagram-toggle');
      const kids = [...t.children].filter(k => getComputedStyle(k).display !== 'none' && k.getBoundingClientRect().width > 0);
      const tops = [...new Set(kids.map(k => Math.round(k.getBoundingClientRect().top)))];
      return { display: getComputedStyle(t).display, layout: t.getAttribute('data-layout'), height: Math.round(t.getBoundingClientRect().height), rows: tops.length, controls: kids.length, spacer: !!t.querySelector('.diagram-toggle-spacer'), first: kids[0]?.className, last: kids[kids.length - 1]?.className };
    });
    console.log(dir, name, JSON.stringify(r));
    await p.close();
  }
  await b.close();
})().catch(e => { console.error('ERR', e.message); process.exit(1); });
