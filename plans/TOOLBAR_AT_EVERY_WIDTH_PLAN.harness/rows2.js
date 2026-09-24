// The scenario toolbar's geometry, out (HEAD) against out-patched (S2): inline on the title line at
// 1400 px (where its controls sit, its padding), and stacked at 880 / 850 / 800 px (how many rows,
// and where the controls after the spacer land once the row wraps). Usage: node rows2.js [dirs] [pages]
const pw = require(require('path').resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const path = require('path');
const DIRS = (process.argv[2] || 'out,out-patched').split(',');
const PAGES = (process.argv[3] || 'TestRunReport.html,TestRunReport_iflow.html').split(',');
const WIDTHS = [1400, 880, 850, 800];
const MEASURE = `() => {
  const t = document.querySelector('details.example-diagrams > summary + .diagram-toggle');
  if (!t) return null;
  const cs = getComputedStyle(t);
  const kids = [...t.children].filter(k => getComputedStyle(k).display !== 'none' && k.getBoundingClientRect().width > 0);
  const tops = [...new Set(kids.map(k => Math.round(k.getBoundingClientRect().top)))].sort((a, b) => a - b);
  const tr = t.getBoundingClientRect(), sr = t.previousElementSibling.getBoundingClientRect();
  const all = [...t.children];
  const spacerIdx = all.findIndex(k => k.classList.contains('diagram-toggle-spacer'));
  const after = all.slice(spacerIdx + 1).filter(k => getComputedStyle(k).display !== 'none' && k.getBoundingClientRect().width > 0);
  const first = kids[0].getBoundingClientRect(), last = kids[kids.length - 1].getBoundingClientRect();
  const wrapped = kids.filter(k => { const r = document.createRange(); r.selectNodeContents(k); return r.getClientRects().length > 1; }).length;
  return {
    layout: t.getAttribute('data-layout') || 'stacked', display: cs.display, wrap: cs.flexWrap, pad: cs.paddingLeft + '/' + cs.paddingRight,
    height: Math.round(tr.height), rows: tops.length, controls: kids.length, labelsWrapped: wrapped,
    toolbarLeft: Math.round(tr.left), toolbarRight: Math.round(tr.right), containerRight: Math.round(sr.right),
    firstLeft: Math.round(first.left), lastRight: Math.round(last.right),
    afterSpacer: after.length ? { first: after[0].className.replace('diagram-toggle-btn', 'btn').trim().slice(0, 24), left: Math.round(after[0].getBoundingClientRect().left), top: Math.round(after[0].getBoundingClientRect().top), sameRowAsFirst: Math.round(after[0].getBoundingClientRect().top) === Math.round(first.top) } : null,
  };
}`;
(async () => {
  const b = await pw.chromium.launch();
  for (const dir of DIRS) for (const name of PAGES) for (const w of WIDTHS) {
    const p = await b.newPage({ viewport: { width: w, height: 900 } });
    await p.route('**/*', r => r.request().url().startsWith('file:') ? r.continue() : r.abort());
    await p.goto('file:///' + path.join(__dirname, dir, name).split(path.sep).join('/'), { waitUntil: 'domcontentloaded' });
    await p.waitForSelector('details.feature');
    await p.evaluate(() => document.querySelectorAll('details').forEach(d => d.open = true));
    await p.waitForTimeout(200);
    const r = await p.evaluate(eval('(' + MEASURE + ')'));
    console.log(dir.padEnd(12), name.replace('.html', '').padEnd(22), String(w).padStart(4), JSON.stringify(r));
    await p.close();
  }
  await b.close();
})().catch(e => { console.error('ERR', e.stack || e.message); process.exit(1); });
