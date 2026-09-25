// The open search help at narrow widths: how far its table runs past the panel's border, and whether the
// panel scrolls it. Usage: node helpspill.js <page.html,...> [scrollbars=0|1]
const pw = require(require('path').resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const path = require('path');
(async () => {
  const b = await pw.chromium.launch(process.argv[3] === '1' ? { ignoreDefaultArgs: ['--hide-scrollbars'] } : {});
  for (const file of process.argv[2].split(',')) {
    const out = [];
    for (const w of (process.env.W || '320,340,360,400,480,600,768').split(',').map(Number)) {
      const p = await b.newPage({ viewport: { width: w, height: 900 } });
      await p.route(/^https?:/, r => r.abort());
      await p.goto('file:///' + path.resolve(file).replace(/\\/g, '/'), { waitUntil: 'domcontentloaded' });
      await p.waitForSelector('details.feature', { state: 'attached' });
      if (w <= 768) await p.locator('.mobile-filter-toggle').click();
      if (process.env.TS === '1') await p.addStyleTag({ content: '*{line-height:1.5 !important;letter-spacing:0.12em !important;word-spacing:0.16em !important}p{margin-bottom:2em !important}' });
      await p.locator('.search-help-toggle').first().click();
      out.push(w + ': ' + await p.evaluate(() => {
        const panel = document.querySelector('.search-help-panel'), table = panel.querySelector('table');
        const pr = panel.getBoundingClientRect(), tr = table.getBoundingClientRect();
        return `table past panel border ${Math.round(tr.right - pr.right)} px, panel ${getComputedStyle(panel).overflowX} ${panel.scrollWidth}/${panel.clientWidth}`;
      }));
      await p.close();
    }
    console.log(path.basename(path.dirname(file)) + '/' + path.basename(file) + '\n  ' + out.join('\n  '));
  }
  await b.close();
})().catch(e => { console.error(e); process.exit(1); });
