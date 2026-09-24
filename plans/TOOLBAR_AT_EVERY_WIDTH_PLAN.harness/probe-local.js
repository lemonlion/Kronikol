// Local overflow and top-bar probe at chosen widths: which boxes hold content wider than themselves
// (clipped or scrolled inside the page, so the page's own scrollWidth never shows it), where each
// scenario toolbar's last control ends against the toolbar's own right edge, and which top-bar
// buttons wrap their label. Usage: node probe-local.js [dir] [page] [widths]
const pw = require(require('path').resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const path = require('path');
const DIR = process.argv[2] || 'out-full';
const PAGE = process.argv[3] || 'TestRunReport_full.html';
const WIDTHS = (process.argv[4] || '780,1000,1400').split(',').map(Number);
const ENGINE = process.env.ENGINE || 'chromium';

const PROBE = `() => {
  const name = el => el.tagName.toLowerCase() + (el.className && typeof el.className === 'string' ? '.' + el.className.trim().split(/\\s+/).join('.') : '');
  const vis = el => el.getBoundingClientRect().width > 0;
  const lines = el => { const r = document.createRange(); r.selectNodeContents(el); return r.getClientRects().length; };
  // Boxes whose content is wider than they are and that clip or scroll it.
  const local = [];
  document.querySelectorAll('body *').forEach(el => {
    if (el.closest('svg') || !vis(el)) return;
    const cs = getComputedStyle(el);
    if (cs.overflowX === 'visible') return;
    if (el.scrollWidth > el.clientWidth + 1) local.push(name(el) + ' +' + (el.scrollWidth - el.clientWidth) + ' (' + cs.overflowX + ')');
  });
  // Each visible scenario toolbar: its right edge, its last control's right edge, labels wrapped.
  const toolbars = [...document.querySelectorAll('.diagram-toggle')].filter(vis).map(t => {
    const r = t.getBoundingClientRect();
    const kids = [...t.querySelectorAll('button, select')].filter(vis);
    const maxRight = Math.max(...kids.map(k => k.getBoundingClientRect().right));
    const wrapped = kids.filter(k => k.tagName === 'BUTTON' && lines(k) > 1).map(k => k.textContent.trim());
    return { layout: t.getAttribute('data-layout') || 'stacked', left: Math.round(r.left), right: Math.round(r.right), lastControlRight: Math.round(maxRight), past: Math.round(maxRight - r.right), controls: kids.length, wrapped: wrapped.join(' | '), display: getComputedStyle(t).display, wrap: getComputedStyle(t).flexWrap };
  });
  const topBar = [...document.querySelectorAll('.toolbar-row button')].filter(vis).map(b => ({ text: b.textContent.trim().slice(0, 40), w: Math.round(b.getBoundingClientRect().width), lines: lines(b) }));
  return { client: document.documentElement.clientWidth, scroll: document.documentElement.scrollWidth, local, toolbars, topBarWrapped: topBar.filter(b => b.lines > 1), topBarCount: topBar.length };
}`;

(async () => {
  const b = await pw[ENGINE].launch();
  for (const w of WIDTHS) {
    const p = await b.newPage({ viewport: { width: w, height: 900 } });
    await p.route('**/*', r => r.request().url().startsWith('file:') ? r.continue() : r.abort());
    await p.goto('file:///' + path.join(__dirname, DIR, PAGE).split(path.sep).join('/'), { waitUntil: 'domcontentloaded' });
    await p.waitForSelector('details.feature', { state: 'attached' });
    await p.evaluate(() => {
      document.querySelectorAll('details.feature, details.scenario, details.example-diagrams').forEach(d => d.open = true);
      const f = document.querySelector('.filters'); if (f && getComputedStyle(f).display === 'none') f.style.display = 'flex';
      document.querySelectorAll('.scenario-diagram-controls').forEach(c => { c.style.display = 'flex'; });
    });
    await p.evaluate(() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))));
    await p.waitForTimeout(40);
    const m = await p.evaluate(eval('(' + PROBE + ')'));
    console.log(`\n== ${DIR}/${PAGE} at ${w} (${ENGINE}): client ${m.client}, scroll ${m.scroll}`);
    console.log('  clipped or scrolled inside the page:', m.local.length ? [...new Set(m.local)].slice(0, 8).join('; ') : 'none');
    m.toolbars.slice(0, 3).forEach((t, i) => console.log(`  toolbar ${i}: ${t.display}/${t.wrap} ${t.layout} ${t.left}-${t.right}, last control ends ${t.lastControlRight} (${t.past > 0 ? '+' + t.past + ' past its edge' : 'inside'}), ${t.controls} controls${t.wrapped ? ', wrapped: ' + t.wrapped : ''}`));
    console.log('  top bar:', m.topBarCount, 'buttons', m.topBarWrapped.length ? 'wrapped: ' + m.topBarWrapped.map(x => `"${x.text}" ${x.w}px ${x.lines} lines`).join('; ') : 'none wrapped');
    await p.close();
  }
  await b.close();
})().catch(e => { console.error(e); process.exit(1); });
