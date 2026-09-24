// Width sweep + cascade probe over the pages gen.cs wrote. Usage: node sweep.js [step] [dir] [pages]
// Reloads at every width because report-init-script.js decides "mobile" once, at load.
// Env: INJECT_CSS='body{font-family:Verdana}' adds a style tag after load (font stress; the E2E lane
// runs on Ubuntu, whose sans-serif is wider than Windows'); TAG=x names the results file.
const pw = require(require('path').resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const path = require('path');
const fs = require('fs');

const OUT = path.join(__dirname, process.argv[3] || 'out');
const STEP = parseInt(process.argv[2] || '10', 10);
const PAGES = (process.argv[4] || 'TestRunReport.html,TestRunReport_noci.html,TestRunReport_iflow.html,Specifications.html,Specifications_iflow.html').split(',');

const MEASURE = `() => {
  const lines = el => { const r = document.createRange(); r.selectNodeContents(el); return r.getClientRects().length; };
  const rightMost = () => {
    let best = null, bestR = -1;
    document.querySelectorAll('body *').forEach(el => {
      if (el.closest('svg')) return;
      const cs = getComputedStyle(el); if (cs.display === 'none' || cs.position === 'fixed') return;
      const r = el.getBoundingClientRect(); if (r.width === 0) return;
      if (r.right > bestR) { bestR = r.right; best = el; }
    });
    return best ? (best.tagName.toLowerCase() + (best.id ? '#' + best.id : '') + (best.className && typeof best.className === 'string' ? '.' + best.className.trim().split(/\\s+/).join('.') : '')) + '@' + Math.round(bestR) : null;
  };
  const box = document.querySelector('.filtering-box');
  const exportCluster = document.querySelector('.filtering-box-export');
  const hr = document.querySelector('.header-row');
  const toggle = document.querySelector('.diagram-toggle');
  const exportWrapped = [...document.querySelectorAll('.export-btn')].map(b => lines(b)).filter(n => n > 1).length;
  const exportMax = Math.max(0, ...[...document.querySelectorAll('.export-btn')].map(b => b.getBoundingClientRect().width));
  const boxInner = box ? box.clientWidth - parseFloat(getComputedStyle(box).paddingLeft) - parseFloat(getComputedStyle(box).paddingRight) : null;
  const ci = document.querySelector('.ci-metadata');
  const topBarWrapped = [...document.querySelectorAll('.toolbar-row button')].map(b => lines(b)).filter(n => n > 1).length;
  const scenarioWrapped = toggle ? [...document.querySelectorAll('.diagram-toggle button')].map(b => lines(b)).filter(n => n > 1).length : -1;
  const boxR = box ? box.getBoundingClientRect() : null;
  const clR = exportCluster ? exportCluster.getBoundingClientRect() : null;
  return {
    inner: window.innerWidth, scroll: document.documentElement.scrollWidth,
    culprit: document.documentElement.scrollWidth > window.innerWidth ? rightMost() : null,
    exportWrapped, topBarWrapped, scenarioWrapped,
    clusterEscapes: boxR && clR ? Math.round(clR.right - boxR.right) : null,
    boxWidth: boxR ? Math.round(boxR.width) : null,
    exportMax: Math.round(exportMax), boxInner: boxInner === null ? null : Math.round(boxInner),
    ciWidth: ci ? Math.round(ci.getBoundingClientRect().width) : null,
    headerDir: hr ? getComputedStyle(hr).flexDirection + '/' + getComputedStyle(hr).flexWrap : null,
    toggleDisplay: toggle ? getComputedStyle(toggle).display : null,
  };
}`;

const OPEN_ALL = `() => {
  document.querySelectorAll('details.feature, details.scenario, details.example-diagrams').forEach(d => d.open = true);
  const f = document.querySelector('.filters'); if (f && f.style.display === 'none') f.style.display = 'flex';
  // At 768 px and below report-init-script.js hides every scenario toolbar behind a "Diagram Settings"
  // button; show them, or the scenario-toolbar columns measure a hidden element.
  document.querySelectorAll('.scenario-diagram-controls').forEach(c => { c.style.display = 'flex'; });
}`;

(async () => {
  const browser = await pw.chromium.launch({ headless: true });
  const results = {};
  for (const name of PAGES) {
    const file = path.join(OUT, name);
    if (!fs.existsSync(file)) { console.log('missing', file); continue; }
    const ctx = await browser.newContext({ viewport: { width: 1400, height: 900 } });
    const page = await ctx.newPage();
    // Keep the sweep offline: the engine fetch is not what is measured.
    await page.route('**/*', r => r.request().url().startsWith('file:') ? r.continue() : r.abort());
    const url = 'file:///' + file.replace(/\\/g, '/');
    const rows = [];
    for (let w = 320; w <= 1400; w += STEP) {
      await page.setViewportSize({ width: w, height: 900 });
      await page.goto(url, { waitUntil: 'domcontentloaded' });
      await page.waitForSelector('details.feature');
      await page.evaluate(eval('(' + OPEN_ALL + ')'));
      if (process.env.INJECT_CSS) await page.addStyleTag({ content: process.env.INJECT_CSS });
      await page.waitForTimeout(30);
      const m = await page.evaluate(eval('(' + MEASURE + ')'));
      rows.push({ w, ...m });
    }
    // Cascade probe at 1400: the violet theme's component overrides.
    await page.setViewportSize({ width: 1400, height: 900 });
    await page.goto(url, { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('details.feature');
    await page.evaluate(eval('(' + OPEN_ALL + ')'));
    const CASCADE = `() => {
      const bg = el => getComputedStyle(el).backgroundColor;
      const mk = cls => { const b = document.createElement('button'); b.className = cls; b.textContent = 'probe'; b.id = 'probe-' + cls.replace(/\\W+/g, '-'); document.body.appendChild(b); return b; };
      const r = {};
      const active = document.querySelector('.details-radio-btn.details-active');
      r.detailsActive = active ? bg(active) : null;
      const dep = document.querySelector('.dependency-toggle');
      r.dependencyIdle = dep ? bg(dep) : null;
      r.diagramToggleActive = bg(mk('diagram-toggle-btn diagram-toggle-active'));
      r.diagramToggleIdle = bg(mk('diagram-toggle-btn'));
      r.iflowToggleActive = bg(mk('iflow-toggle-btn iflow-toggle-active'));
      const t = document.querySelector('.diagram-toggle');
      r.toggleDisplay = t ? getComputedStyle(t).display : null;
      r.toggleBtnFont = t && t.querySelector('.diagram-toggle-btn') ? getComputedStyle(t.querySelector('.diagram-toggle-btn')).fontSize : null;
      return r;
    }`;
    const cascade = await page.evaluate(eval('(' + CASCADE + ')'));
    const hoverBg = async sel => { await page.hover(sel); await page.waitForTimeout(50); return page.$eval(sel, el => getComputedStyle(el).backgroundColor); };
    cascade.diagramToggleActiveHover = await hoverBg('#probe-diagram-toggle-btn-diagram-toggle-active');
    cascade.diagramToggleIdleHover = await hoverBg('#probe-diagram-toggle-btn');
    cascade.iflowToggleActiveHover = await hoverBg('#probe-iflow-toggle-btn-iflow-toggle-active');
    if (await page.$('.details-radio-btn.details-active')) cascade.detailsActiveHover = await hoverBg('.details-radio-btn.details-active');
    if (await page.$('.dependency-toggle')) cascade.dependencyHover = await hoverBg('.dependency-toggle');
    results[name] = { rows, cascade };
    await ctx.close();

    // Summaries
    const overflow = rows.filter(r => r.scroll > r.inner + 1);
    const bands = [];
    for (const r of overflow) { const b = bands[bands.length - 1]; if (b && r.w === b.to + STEP) { b.to = r.w; b.max = Math.max(b.max, r.scroll - r.inner); } else bands.push({ from: r.w, to: r.w, max: r.scroll - r.inner, culprit: r.culprit }); }
    const exportWrap = rows.filter(r => r.exportWrapped > 0).map(r => r.w);
    const escape = rows.filter(r => r.clusterEscapes > 1).map(r => r.w);
    const scen = rows.filter(r => r.scenarioWrapped > 0).map(r => r.w);
    const top = rows.filter(r => r.topBarWrapped > 0).map(r => r.w);
    console.log(`\n== ${name}`);
    console.log('  sideways scroll bands:', bands.length ? bands.map(b => `${b.from}-${b.to} (max +${b.max}px, ${b.culprit})`).join('; ') : 'none');
    console.log('  export label wraps at:', exportWrap.length ? `${exportWrap[0]}-${exportWrap[exportWrap.length - 1]} (${exportWrap.length} widths)` : 'never');
    console.log('  export cluster escapes box at:', escape.length ? `${escape[0]}-${escape[escape.length - 1]}` : 'never');
    console.log('  top-bar label wraps at:', top.length ? `${top[0]}-${top[top.length - 1]} (${top.length})` : 'never');
    console.log('  scenario-toolbar label wraps at:', scen.length ? `${scen[0]}-${scen[scen.length - 1]} (${scen.length})` : 'never');
    console.log('  header-row at 900:', rows.find(r => r.w === 900)?.headerDir, ' box width at 900/1000/1200:', [900, 1000, 1200].map(w => rows.find(r => r.w === w)?.boxWidth).join('/'));
    console.log('  at 320: box inner width', rows[0].boxInner, ' widest export button', rows[0].exportMax, ' CI box width', rows[0].ciWidth, '; CI box at 1400:', rows[rows.length - 1].ciWidth);
    console.log('  .diagram-toggle display at 1400:', rows[rows.length - 1].toggleDisplay);
    console.log('  cascade:', JSON.stringify(cascade));
  }
  fs.writeFileSync(path.join(__dirname, 'sweep-results-' + (process.env.TAG || process.argv[3] || 'out') + '.json'), JSON.stringify(results, null, 1));
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
