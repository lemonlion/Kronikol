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
  'opacity', 'list-style-type', 'vertical-align', 'white-space', 'text-decoration-line', 'cursor', 'overflow-x',
  'position', 'top', 'z-index', 'width', 'height', 'column-gap', 'row-gap', 'align-items', 'justify-content', 'flex-direction', 'content'];
// what to compare, by surface: a name and a selector run inside the first scenario that has the thing (or the page)
const PICKS = {
  2: [['band', '.run-band'], ['ring', '.run-ring'], ['run title', '.run-summary .run-title'], ['run status', '.run-status'], ['run when', '.run-summary .run-when'],
      ['stat dt', '.run-summary .run-stat dt'], ['stat dd', '.run-summary .run-stat dd'], ['stat fail dd', '.run-stat-fail dd'], ['ci zone', '.run-ci'],
      ['ci title', '.run-ci .run-title'], ['ci dt', '.run-ci .run-stat dt'], ['ci dd', '.run-ci .run-stat dd'], ['commit', '.run-stat dd code'], ['run link', '.run-link'], ['filter box', '.filtering-box'],
      ['filter header', '.filtering-box-header'], ['filter title', '.filtering-box-header h2'], ['export row', '.filtering-box-export'], ['export button', '.filtering-box-export .export-btn']],
  // the filter rows (S5 and S6): labels, segments, chips, mode switches, the readout and the search field, as loaded
  fr: [['status row', '.status-filters'], ['status label', '.status-filters-label'], ['status controls', '.status-filters .fltr-controls'],
      ['status first', '.status-toggle'], ['status last', '.status-toggle:last-of-type'], ['duration label', '.duration-filters-label'],
      ['percentile', '.percentile-btn'], ['happy row', '.happy-filters'], ['happy label', '.happy-filters-label'],
      ['happy seg on', '.happy-seg[aria-pressed="true"]'], ['happy seg off', '.happy-seg[aria-pressed="false"]'],
      ['tags row', '.tag-filters'], ['tags label', '.tag-filters-label'], ['tags controls', '.tag-filters .fltr-controls'],
      ['tag mode', '.tag-mode-toggle'], ['tag chip', '.tag-toggle'], ['deps row', '.dependency-filters'], ['deps label', '.dependency-filters-label'],
      ['deps controls', '.dependency-filters .fltr-controls'], ['dep mode', '.dep-mode-toggle'], ['dep chip', '.dependency-toggle'],
      ['readout', '.filter-readout'], ['search', '#searchbar']],
  // More filters and the bar's filters (S7), as loaded: the button, the bar's top row, its group and readout
  nb: [['more filters', '.more-filters'], ['more label', '.more-filters-label'], ['search line', '.filter-search'], ['nav row', '.nav-row'],
      ['nav tabs', '.nav-row > .report-tabs'], ['nav filters', '.nav-filters'], ['nav readout', '.nav-readout'], ['nav readout long', '.nav-readout-long']],
  5: [['bar', '.toolbar-row.report-nav'], ['tabs', '.report-tabs'], ['tab on', '.report-tab[aria-selected="true"]'], ['tab off', '.report-tab[aria-selected="false"]'],
      ['tab count', '.report-tab[aria-selected="true"] .tab-n'], ['tools', '.features-tools'], ['panels', '.report-panels']],
  // a third element is the branch's selector where its markup names the thing differently
  6: [['tools line', '.features-tools-line'], ['view switch', '.view-switch'], ['view on', '.view-btn[aria-selected="true"]'], ['view off', '.view-btn[aria-selected="false"]'],
      ['fold group', '.k-fold-group', '.fold-group'], ['fold label', '.k-fold-label', '.fold-label'], ['fold button', '.k-fold-expand', '.fold-expand'],
      ['fold idle', '.k-fold-btn[aria-disabled="true"]', '.fold-btn[aria-disabled="true"]'], ['spacer', '.tools-spacer'], ['sort group', '.sort-group'],
      ['sort label', '.sort-label'], ['sort select', '#features-sort'], ['display settings', '.features-tools .toolbar-right']],
  '9b': [['table card', '.features-table-view'], ['table wrapper', '.features-table-view .features-summary-table-wrapper'], ['table', '.feature-summary-table'],
      ['group th', '.feature-summary-table tr.table-groups th:not(:first-child)'], ['group th start', '.feature-summary-table tr.table-groups th.grp-start'],
      ['header th', '.feature-summary-table thead tr:last-child th:first-child'], ['header th num', '.feature-summary-table thead tr:last-child th:nth-child(2)'],
      ['name td', '.feature-summary-table tbody tr:not(.failed) td:first-child'], ['num td', '.feature-summary-table tbody tr:not(.failed) td:nth-child(2)'],
      ['grp td', '.feature-summary-table tbody tr:not(.failed) td.grp-start'], ['failed name td', '.feature-summary-table tbody tr.failed td:first-child'],
      ['failed count td', '.feature-summary-table tbody tr.failed td:nth-child(4)']],
  // the payload controls in the report's bar (S8): a fourth element names a pseudo-element, a null third the same selector
  '7a': [['rb radio', '.toolbar-right .details-radio'], ['rb label', '.toolbar-right .details-radio-label'],
      ['rb btn', '.toolbar-right .details-radio > .details-radio-btn:not(.details-active)'], ['rb btn on', '.toolbar-right .details-radio > .details-radio-btn.details-active'],
      ['rb btn label', '.toolbar-right .details-radio-btn .k-lbl', '.toolbar-right .details-radio-btn .btn-label'],
      ['rb btn label bold', '.toolbar-right .details-radio-btn .k-lbl', '.toolbar-right .details-radio-btn .btn-label', '::after'],
      ['rb lines', '.toolbar-right .truncate-lines-select'], ['rb format zone', '.toolbar-right .diagram-format'],
      ['rb format', '.toolbar-right .note-format-select'], ['rb width', '.toolbar-right .note-width-select'],
      ['rb flags', '.toolbar-right .diagram-flags'], ['rb chip off', '.toolbar-right .toggle-btn[aria-pressed="false"]'],
      ['rb chip on', '.toolbar-right .toggle-btn[aria-pressed="true"]'], ['rb chip dot', '.toolbar-right .toggle-btn[aria-pressed="false"]', null, '::before'],
      ['rb chip dot on', '.toolbar-right .toggle-btn[aria-pressed="true"]', null, '::before'],
      ['rb format head', '.toolbar-right .diagram-format', null, '::before'], ['rb flags head', '.toolbar-right .diagram-flags', null, '::before'],
      ['rb settings', '.features-tools .toolbar-right']],
  // a scenario's diagram section, its tabs and its settings (S8); 14o reads them opened (run it on its own)
  '14': [['section', 'details.example-diagrams'], ['section summary', 'details.example-diagrams > summary'],
      ['tabs bar', '.diagram-toggle-tabs'], ['tab on', '.diagram-toggle-tabs .diagram-toggle-active'],
      ['tab off', '.diagram-toggle-tabs .diagram-toggle-btn:not(.diagram-toggle-active)'],
      ['settings', '.k-scn-settings', '.scenario-settings'], ['gear', '.k-scn-gear', '.scenario-settings-gear'],
      ['gear svg', '.k-scn-gear > svg', '.scenario-settings-gear > svg'], ['options bar', '.diagram-options-bar'],
      ['this scenario', '.k-this-scenario', '.scenario-settings-label'], ['ob radio', '.diagram-options-bar .details-radio'],
      ['ob label', '.diagram-options-bar .details-radio-label'], ['ob btn', '.diagram-options-bar .details-radio > .details-radio-btn:not(.details-active)'],
      ['ob btn on', '.diagram-options-bar .details-radio > .details-radio-btn.details-active'], ['ob lines', '.diagram-options-bar .truncate-lines-select'],
      ['ob format zone', '.diagram-options-bar .diagram-format'], ['ob format', '.diagram-options-bar .note-format-select'],
      ['ob width', '.diagram-options-bar .note-width-select'], ['ob flags', '.diagram-options-bar .diagram-flags'],
      ['ob chip off', '.diagram-options-bar .toggle-btn[aria-pressed="false"]'], ['ob chip on', '.diagram-options-bar .toggle-btn[aria-pressed="true"]'],
      ['ob format head', '.diagram-options-bar .diagram-format', null, '::before'], ['ob flags head', '.diagram-options-bar .diagram-flags', null, '::before'],
      ['summary gear', '.gear-in-summary']],
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
PICKS['14o'] = PICKS['14'];
(async () => {
  const surfaces = surfaceList.split(',');
  // 14o: the first scenario's settings opened, by its gear (on a phone, the one on the section's title)
  const open = !surfaces.includes('14o') ? null : WIDTH <= 768 ? ['.gear-in-summary', '.gear-in-summary'] : ['.k-scn-gear', '.scenario-settings-gear'];
  const b = await pw[browserName].launch(browserName === 'chromium' ? { channel: 'chrome' } : { executablePath: 'C:/Users/cex/AppData/Local/ms-playwright/firefox-1511/firefox/firefox.exe' });
  async function read(file, branch) {
    // This Firefox build takes no viewport from the driver, so it reads at its window's width; given a width, the page is
    // read inside a frame of that width, beside it (media queries follow the frame's width)
    const framed = browserName === 'firefox' && !!widthArg;
    const ctx = await b.newContext(browserName === 'firefox' ? { viewport: null } : { viewport: { width: WIDTH, height: 900 } });
    await ctx.addInitScript(t => { try { localStorage.setItem('kronikol.theme', t); } catch (e) {} }, theme);
    const p = await ctx.newPage();
    let t = p;
    if (framed) {
      const wrapper = path.join(path.dirname(path.resolve(file)), '__framed-' + path.basename(file));
      require('fs').writeFileSync(wrapper, `<!doctype html><body style="margin:0"><iframe src="${path.basename(file)}" style="width:${WIDTH}px;height:900px;border:0"></iframe>`);
      await p.goto('file:///' + wrapper.split(path.sep).join('/'));
      await p.waitForTimeout(2500);
      t = p.frames().find(f => f !== p.mainFrame());
    } else {
      await p.goto('file:///' + path.resolve(file).split(path.sep).join('/'));
      await p.waitForTimeout(2500);
    }
    // a page busy drawing its diagrams produces no frames for seconds, and a transition does not advance without them,
    // so a control read after a click could still show where its transition started: both pages read without them
    await t.addStyleTag({ content: '*, *::before, *::after { transition: none !important; }' });
    if (open) { await t.evaluate(sel => { const g = document.querySelector(sel); if (g) g.click(); }, open[branch ? 1 : 0]); await p.waitForTimeout(300); }
    const out = await t.evaluate(([picks, props, branch]) => {
      // open every feature, rule and scenario and their steps, so closed details still have styles to read
      document.querySelectorAll('details.feature, details.rule, details.scenario, details.scenario-steps, details.scenario-background, details.failure-result').forEach(d => { d.open = true; });
      const res = {};
      for (const [name, previewSel, branchSel, pseudo] of picks) {
        const sel = branch && branchSel ? branchSel : previewSel;
        const el = document.querySelector(sel);
        if (!el) { res[name] = null; continue; }
        const cs = getComputedStyle(el, pseudo || null);
        const v = {};
        for (const pr of props) v[pr] = cs.getPropertyValue(pr);
        const where = el.closest('details.scenario');
        res[name] = { where: where ? where.id : '', v };
      }
      return res;
    }, [surfaces.flatMap(s => PICKS[s] || []), PROPS, branch]);
    await ctx.close();
    return out;
  }
  const a = await read(previewFile, false), c = await read(branchFile, true);
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
