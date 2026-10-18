// Computed-style parity between the preview (the mock over the parity run) and the branch's rendering of the same run,
// for the surfaces a stage ports: the same elements are found in both pages by scenario id and position, and their
// computed styles compared property by property.
// usage: node style-parity.js <preview.html> <branch.html> <light|dark> <surfaces, e.g. 0,9,10,11,12,13> [browser]
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const path = require('path');
const [previewFile, branchFile, theme, surfaceList, browserName = 'chromium', widthArg] = process.argv.slice(2);
const WIDTH = +widthArg || 1400;
const PROPS = ['color', 'background-color', 'font-family', 'font-size', 'font-weight', 'line-height', 'letter-spacing', 'text-transform',
  'padding-top', 'padding-right', 'padding-bottom', 'padding-left', 'margin-top', 'margin-right', 'margin-bottom', 'margin-left',
  'border-top-width', 'border-top-style', 'border-top-color', 'border-right-width', 'border-bottom-width', 'border-bottom-style', 'border-bottom-color',
  'border-left-width', 'border-left-style', 'border-left-color', 'border-top-left-radius', 'border-bottom-right-radius', 'box-shadow', 'display',
  'opacity', 'list-style-type', 'vertical-align', 'white-space', 'text-decoration-line', 'cursor', 'overflow-x'];
// what to compare, by surface: a name and a selector run inside the first scenario that has the thing (or the page)
const PICKS = {
  2: [['band', '.run-band'], ['ring', '.run-ring'], ['run title', '.run-summary .run-title'], ['run status', '.run-status'], ['run when', '.run-summary .run-when'],
      ['stat dt', '.run-summary .run-stat dt'], ['stat dd', '.run-summary .run-stat dd'], ['stat fail dd', '.run-stat-fail dd'], ['ci zone', '.run-ci'],
      ['ci title', '.run-ci .run-title'], ['ci dt', '.run-ci .run-stat dt'], ['ci dd', '.run-ci .run-stat dd'], ['commit', '.run-stat dd code'], ['run link', '.run-link'], ['filter box', '.filtering-box'],
      ['filter header', '.filtering-box-header'], ['filter title', '.filtering-box-header h2'], ['export row', '.filtering-box-export'], ['export button', '.filtering-box-export .export-btn']],
  0: [['body', 'body'], ['h1', 'h1'], ['jump', '.jump-to-failure'], ['top', '.back-to-top']],
  9: [['feature', 'details.feature'], ['feature summary', 'details.feature > summary'], ['feature failed summary', 'details.feature > summary.failed'],
      ['scenario', 'details.scenario'], ['scenario summary', 'details.scenario > summary'], ['scenario failed summary', 'details.scenario > summary.failed'],
      ['rule', 'details.rule'], ['rule summary', 'details.rule > summary'], ['feature description', '.feature-description']],
  10: [['label', 'details.scenario > summary .label'], ['duration', 'details.scenario > summary .duration-badge'], ['copy', '.copy-scenario-name'],
       ['link', '.scenario-link'], ['endpoint', '.endpoint']],
  11: [['failure', 'details.failure-result'], ['failure summary', 'details.failure-result > summary'], ['failure pre', 'details.failure-result > pre'],
       ['diff', '.error-diff'], ['diff label', '.diff-label'], ['diff expected', '.diff-expected'], ['diff actual', '.diff-actual']],
  12: [['steps', 'details.scenario-steps'], ['steps summary', 'details.scenario-steps > summary'], ['step', '.step'], ['step keyword', '.step-keyword'],
       ['step text', '.step-text'], ['step duration', '.step-duration'], ['step status', '.step-status'], ['step collapsible', '.step-collapsible > summary'],
       ['background', 'details.scenario-background'], ['background summary', 'details.scenario-background > summary'], ['doc string', '.doc-string']],
  13: [['param wrapper', '.param-table-wrapper'], ['param table', '.param-test-table'], ['param th', '.param-test-table th'],
       ['param master th', '.param-test-table th.master-header'], ['param td', '.param-test-table td'], ['status badge', '.status-badge'],
       ['step table', '.step-param-table'], ['step table th', '.step-param-table th'], ['step table td', '.step-param-table td'],
       ['inline value', '.step-param-inline'], ['expand', 'details.param-expand > summary']],
};
(async () => {
  const surfaces = surfaceList.split(',');
  const b = await pw[browserName].launch(browserName === 'chromium' ? { channel: 'chrome' } : { executablePath: 'C:/Users/cex/AppData/Local/ms-playwright/firefox-1511/firefox/firefox.exe' });
  async function read(file) {
    const ctx = await b.newContext(browserName === 'firefox' ? { viewport: null } : { viewport: { width: WIDTH, height: 900 } });
    await ctx.addInitScript(t => { try { localStorage.setItem('kronikol.theme', t); } catch (e) {} }, theme);
    const p = await ctx.newPage();
    await p.goto('file:///' + path.resolve(file).split(path.sep).join('/'));
    await p.waitForTimeout(2500);
    const out = await p.evaluate(([picks, props]) => {
      // open every feature, rule and scenario and their steps, so closed details still have styles to read
      document.querySelectorAll('details.feature, details.rule, details.scenario, details.scenario-steps, details.scenario-background, details.failure-result').forEach(d => { d.open = true; });
      const res = {};
      for (const [name, sel] of picks) {
        const el = document.querySelector(sel);
        if (!el) { res[name] = null; continue; }
        const cs = getComputedStyle(el);
        const v = {};
        for (const pr of props) v[pr] = cs.getPropertyValue(pr);
        const where = el.closest('details.scenario');
        res[name] = { where: where ? where.id : '', v };
      }
      return res;
    }, [surfaces.flatMap(s => PICKS[s] || []), PROPS]);
    await ctx.close();
    return out;
  }
  const a = await read(previewFile), c = await read(branchFile);
  let total = 0;
  for (const s of surfaces) for (const [name] of PICKS[s] || []) {
    if (!a[name] || !c[name]) { console.log(`[${s}] ${name}: ${!a[name] ? 'missing in preview' : ''} ${!c[name] ? 'missing in branch' : ''}`); continue; }
    const diffs = PROPS.filter(pr => a[name].v[pr] !== c[name].v[pr]);
    total += diffs.length;
    if (diffs.length) console.log(`[${s}] ${name} (${diffs.length}): ` + diffs.map(pr => `${pr} ${a[name].v[pr]} | ${c[name].v[pr]}`).join('; '));
  }
  console.log(browserName, theme, WIDTH, 'differences', total);
  await b.close();
})();
