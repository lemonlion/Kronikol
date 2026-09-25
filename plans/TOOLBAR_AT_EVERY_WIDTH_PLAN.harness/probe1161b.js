// The D5 edge: text spacing + classic scrollbar at the first in-row widths. With an optional candidate rule
// injected (INJECT), does the export cluster stay inside its box, and which labels wrap?
// Usage: [INJECT='css'] node probe1161b.js <page.html,...>
const pw = require(require('path').resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const path = require('path');
const TS = '*{line-height:1.5 !important;letter-spacing:0.12em !important;word-spacing:0.16em !important}p{margin-bottom:2em !important}';
(async () => {
  const b = await pw.chromium.launch({ ignoreDefaultArgs: ['--hide-scrollbars'] });
  for (const file of process.argv[2].split(',')) {
    console.log('== ' + path.basename(file) + (process.env.INJECT ? ' (candidate)' : ''));
    for (const w of [1150, 1160, 1161, 1163, 1165, 1168, 1170, 1172, 1175, 1180, 1200]) {
      const p = await b.newPage({ viewport: { width: w, height: 900 } });
      await p.route(/^https?:/, r => r.abort());
      await p.goto('file:///' + path.resolve(file).replace(/\\/g, '/'), { waitUntil: 'domcontentloaded' });
      await p.evaluate(() => document.querySelectorAll('details').forEach(d => d.open = true));
      await p.addStyleTag({ content: TS });
      if (process.env.INJECT) await p.addStyleTag({ content: process.env.INJECT });
      await p.evaluate(() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))));
      console.log(' ', w, await p.evaluate(() => {
        const de = document.documentElement, box = document.querySelector('.filtering-box'), cl = document.querySelector('.filtering-box-export');
        const lines = el => { const r = document.createRange(); r.selectNodeContents(el); return r.getClientRects().length; };
        const btns = [...cl.querySelectorAll('button')].map(x => `${x.textContent.trim()}:${Math.round(x.getBoundingClientRect().width)}${lines(x) > 1 ? '/' + lines(x) + 'L' : ''}`).join(' ');
        const boxR = box.getBoundingClientRect(), inner = boxR.right - parseFloat(getComputedStyle(box).paddingRight);
        return `page +${de.scrollWidth - de.clientWidth} | box ${Math.round(boxR.width)} | cluster past box ${Math.round(cl.getBoundingClientRect().right - boxR.right)}, past content ${Math.round(cl.getBoundingClientRect().right - inner)} | ${btns}`;
      }));
      await p.close();
    }
  }
  await b.close();
})().catch(e => { console.error(e); process.exit(1); });
