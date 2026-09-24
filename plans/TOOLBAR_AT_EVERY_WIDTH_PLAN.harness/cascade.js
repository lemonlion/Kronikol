// Clean cascade probe: short page (nothing opened), real controls where they exist, synthetic
// buttons for the classes the fixture has no markup for. Usage: node cascade.js [dir]
const pw = require(require('path').resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const path = require('path');
const fs = require('fs');
const DIR = path.join(__dirname, process.argv[2] || 'out');
const PAGES = ['TestRunReport.html', 'TestRunReport_iflow.html', 'Specifications.html', 'Specifications_iflow.html'];

(async () => {
  const b = await pw.chromium.launch();
  for (const name of PAGES) {
    const file = path.join(DIR, name);
    if (!fs.existsSync(file)) continue;
    const p = await b.newPage({ viewport: { width: 1400, height: 900 } });
    await p.route('**/*', r => r.request().url().startsWith('file:') ? r.continue() : r.abort());
    await p.goto('file:///' + file.replace(/\\/g, '/'), { waitUntil: 'domcontentloaded' });
    await p.waitForSelector('details.feature');
    await p.evaluate(() => {
      const host = document.querySelector('.toolbar-row') || document.body;
      const mk = (id, cls) => { const x = document.createElement('button'); x.className = cls; x.id = id; x.textContent = 'probe'; host.appendChild(x); };
      mk('pa', 'diagram-toggle-btn diagram-toggle-active');
      mk('pi', 'diagram-toggle-btn');
      mk('pf', 'iflow-toggle-btn iflow-toggle-active');
    });
    const bg = sel => p.$eval(sel, el => getComputedStyle(el).backgroundColor);
    const hov = async sel => { await p.mouse.move(0, 0); await p.hover(sel); await p.waitForTimeout(40); return bg(sel); };
    const row = { page: name };
    row.detailsActive = await bg('.details-radio-btn.details-active');
    row.detailsActiveHover = await hov('.details-radio-btn.details-active');
    row.tabActive = await bg('#pa'); row.tabActiveHover = await hov('#pa');
    row.tabIdle = await bg('#pi'); row.tabIdleHover = await hov('#pi');
    row.iflowActive = await bg('#pf'); row.iflowActiveHover = await hov('#pf');
    row.exportHover = await hov('.export-btn');
    row.toggleDisplay = await p.evaluate(() => { document.querySelectorAll('details').forEach(d => d.open = true); const t = document.querySelector('.diagram-toggle'); return t ? getComputedStyle(t).display : null; });
    console.log(JSON.stringify(row));
    await p.close();
  }
  await b.close();
})().catch(e => { console.error('ERR', e.message); process.exit(1); });
