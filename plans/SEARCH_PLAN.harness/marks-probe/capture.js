// Captures the two drawn diagrams (JSON payload notes; long-note foreignObject box) from the live demo report.
const fs = require('fs');
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const URL = 'https://lemonlion.github.io/BreakfastProvider/reports/reqnroll/TestRunReport.html';
const SCENS = {
  muffin: 'scenario-a-valid-apple-cinnamon-muffin-request-should-return-a-fresh-batch',
  openapi: 'scenario-the-openapi-endpoint-should-return-a-valid-specification',
};
(async () => {
  const b = await pw.chromium.launch();
  const page = await (await b.newContext({ viewport: { width: 1400, height: 1000 } })).newPage();
  await page.goto(URL, { waitUntil: 'load', timeout: 120000 });
  const meta = await page.evaluate(() => ({ generator: (document.querySelector('meta[name=generator]') || {}).content || null, version: (document.body.innerHTML.match(/Kronikol\s+v?(\d+\.\d+\.\d+)/) || [])[1] || null }));
  await page.evaluate(() => {
    const sel = [...document.querySelectorAll('select.note-format-select')].find(s => !s.closest('details.feature'));
    sel.value = [...sel.options].find(o => /json/i.test(o.textContent)).value;
    sel.dispatchEvent(new Event('change', { bubbles: true }));
    [...document.querySelectorAll('button.details-radio-btn')].find(x => !x.closest('details.feature') && /Expanded/.test(x.textContent)).click();
  });
  const out = { url: URL, meta, capturedAt: new Date().toISOString(), diagrams: {} };
  for (const [key, id] of Object.entries(SCENS)) {
    await page.evaluate((id) => { const sc = document.getElementById(id); for (let e = sc; e; e = e.parentElement) if (e.tagName === 'DETAILS') e.open = true; sc.scrollIntoView(); }, id);
    await page.waitForSelector(`#${id} .diagram-view-seq .plantuml-browser svg`, { timeout: 120000 });
    await page.waitForTimeout(1500);
    // make sure any long-note box is filled (they fill when near the window)
    await page.evaluate((id) => { const fo = document.querySelector(`#${id} .diagram-view-seq .plantuml-browser svg foreignObject`); if (fo) fo.scrollIntoView({ block: 'center' }); }, id);
    await page.waitForTimeout(1500);
    const d = await page.evaluate((id) => {
      const pb = document.querySelector(`#${id} .diagram-view-seq .plantuml-browser`);
      const svg = pb.querySelector('svg');
      const els = [pb, svg, ...svg.querySelectorAll('*')];
      const rules = [];
      const visit = (list, media) => {
        for (const r of list) {
          if (r.cssRules && !r.selectorText) { visit(r.cssRules, r.media ? r.media.mediaText : (r.conditionText || media)); continue; }
          if (!r.selectorText) continue;
          let hit = false;
          try { hit = els.some(e => e.matches(r.selectorText)); } catch (e) { }
          if (hit) rules.push({ media, sel: r.selectorText, css: r.style.cssText });
        }
      };
      for (const ss of document.styleSheets) { try { visit(ss.cssRules, null); } catch (e) { } }
      const cs = getComputedStyle(svg);
      const vars = {};
      const used = new Set();
      for (const r of rules) for (const m of r.css.matchAll(/var\((--[\w-]+)/g)) used.add(m[1]);
      const pcs = getComputedStyle(pb);
      for (const v of used) vars[v] = pcs.getPropertyValue(v).trim();
      return {
        scenario: id, containerId: pb.id, containerWidth: pb.clientWidth,
        svg: svg.outerHTML,
        svgComputed: { width: cs.width, height: cs.height, maxWidth: cs.maxWidth, display: cs.display },
        rules, vars,
      };
    }, id);
    out.diagrams[key] = d;
    console.log(key, d.containerId, 'svg bytes', d.svg.length, 'rules', d.rules.length, JSON.stringify(d.svgComputed));
    for (const r of d.rules) console.log('   ', r.media ? '@' + r.media + ' ' : '', r.sel.slice(0, 120), '{', r.css.slice(0, 160), '}');
  }
  fs.writeFileSync('capture.json', JSON.stringify(out));
  console.log('meta', JSON.stringify(meta));
  await b.close();
})().catch(e => { console.error(e); process.exit(1); });
