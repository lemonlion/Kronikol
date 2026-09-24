// The gap between a grouped parameter table's bottom border and what follows it, with and without the wrapper,
// on the published LightBDD report (out-published/, section N) with the release's stylesheet injected.
const pw = require(require('path').resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const path = require('path');
const fs = require('fs');
const css = fs.readFileSync(path.resolve(__dirname, '../../src/Kronikol/Reports/stylesheets.css'), 'utf8');
(async () => {
  const b = await pw.chromium.launch();
  for (const wrap of [false, true]) {
    const p = await b.newPage({ viewport: { width: 1400, height: 900 } });
    await p.route(/^https?:/, r => r.abort());
    await p.goto('file:///' + path.resolve(__dirname, 'out-published/lightbdd_TestRunReport.html').split(path.sep).join('/'), { waitUntil: 'domcontentloaded' });
    await p.addStyleTag({ content: css });
    if (wrap) await p.evaluate(() => document.querySelectorAll('table.param-test-table').forEach(t => {
      if (t.parentElement.classList.contains('param-table-wrapper')) return;
      const w = document.createElement('div'); w.className = 'param-table-wrapper';
      t.parentElement.insertBefore(w, t); w.appendChild(t);
    }));
    await p.evaluate(() => document.querySelectorAll('details.feature, details.scenario').forEach(d => d.open = true));
    const g = p.locator('details.scenario-parameterized:has-text("Different muffin recipes")').first();
    await g.scrollIntoViewIfNeeded();
    const r = await g.evaluate(s => {
      const t = s.querySelector('table.param-test-table');
      const next = s.querySelector('.param-detail-panels');
      const sum = s.querySelector(':scope > summary');
      return `summary->table ${(t.getBoundingClientRect().top - sum.getBoundingClientRect().bottom).toFixed(1)} px, table->detail panels ${(next.getBoundingClientRect().top - t.getBoundingClientRect().bottom).toFixed(1)} px`;
    });
    console.log(wrap ? 'wrapped  ' : 'unwrapped', r);
    await p.close();
  }
  await b.close();
})();
