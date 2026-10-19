// ::before parity for the base surfaces' drawn marks (triangles, the failed cross, the Rule label).
// usage: node pseudo-parity.js <preview.html> <branch.html> <light|dark> [width]
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require('../../../tools/playwright/installed-browser.js');
const path = require('path');
const [previewFile, branchFile, theme, widthArg] = process.argv.slice(2);
const PICKS = [['steps summary', 'details.scenario-steps > summary'], ['failure summary', 'details.failure-result > summary'],
  ['diagrams summary', 'details.example-diagrams > summary'], ['step collapsible', '.scenario-steps > .step-collapsible > summary'],
  ['scenario failed', 'details.scenario > summary.h3.failed'], ['feature failed', 'details.feature > summary.h2.failed'], ['rule', 'details.rule > summary.h2-5'],
  ['param expand', 'details.param-expand > summary']];
const PROPS = ['content', 'display', 'position', 'left', 'top', 'margin-top', 'margin-right', 'width', 'font-size', 'font-weight', 'line-height', 'color', 'transform', 'text-transform', 'letter-spacing'];
(async () => {
  const b = await launchInstalled(pw.chromium, { channel: 'chrome' });
  async function read(file) {
    const ctx = await b.newContext({ viewport: { width: +widthArg || 1400, height: 900 } });
    await ctx.addInitScript(t => { try { localStorage.setItem('kronikol.theme', t); } catch (e) {} }, theme);
    const p = await ctx.newPage();
    await p.goto('file:///' + path.resolve(file).split(path.sep).join('/')); await p.waitForTimeout(2500);
    const r = await p.evaluate(([picks, props]) => {
      document.querySelectorAll('details.feature, details.rule, details.scenario, details.scenario-steps, details.failure-result').forEach(d => { d.open = true; });
      const out = {};
      for (const [n, s] of picks) { const e = document.querySelector(s); if (!e) { out[n] = null; continue; } const cs = getComputedStyle(e, '::before'); out[n] = Object.fromEntries(props.map(x => [x, cs.getPropertyValue(x)])); }
      return out;
    }, [PICKS, PROPS]);
    await ctx.close(); return r;
  }
  const a = await read(previewFile), c = await read(branchFile);
  let total = 0;
  for (const [n] of PICKS) {
    if (!a[n] || !c[n]) { console.log(n, !a[n] ? 'missing in preview' : '', !c[n] ? 'missing in branch' : ''); continue; }
    const d = PROPS.filter(x => a[n][x] !== c[n][x]); total += d.length;
    if (d.length) console.log(n, '(' + d.length + '):', d.map(x => `${x} ${a[n][x]} | ${c[n][x]}`).join('; '));
  }
  console.log('pseudo', theme, 'differences', total);
  await b.close();
})();
