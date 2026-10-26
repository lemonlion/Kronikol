'use strict';
// I7: at a phone width, how wide the report sheet's zones and selects are against the sheet, beside the same for a
// scenario's sheet, and how wide the Timeline's control groups and their buttons are against the controls.
// usage: node probe/i7-phone.js <main.html> <outDir> [width=375]
const path = require('path');
const { open, frames, openScenario, write } = require('./common');

const widths = () => {
  const box = (el) => { if (!el) return null; const r = el.getBoundingClientRect(), s = getComputedStyle(el);
    return { w: +r.width.toFixed(1), content: +(el.clientWidth - parseFloat(s.paddingLeft) - parseFloat(s.paddingRight)).toFixed(1), left: +r.left.toFixed(1) }; };
  return box;
};

(async () => {
  const [file, outDir, w] = process.argv.slice(2);
  const width = +(w || 375);
  const { browser, page } = await open(file, width, 812, 'light');
  const out = { width };
  try {
    // the report's display settings, behind the bar's gear on a phone
    await page.evaluate(() => { const g = document.querySelector('.tools-gear'); if (!g) throw new Error('no .tools-gear'); g.click(); });
    await frames(page, 4);
    out.report = await page.evaluate(`(() => {
      const box = (${widths})();
      const sheet = document.querySelector('#display-settings');
      return { sheet: box(sheet), alignItems: getComputedStyle(sheet).alignItems, rowGap: getComputedStyle(sheet).rowGap,
        zones: Array.from(sheet.children).map((z) => ({ cls: z.className, ...box(z) })),
        selects: Array.from(sheet.querySelectorAll('select')).map((s) => ({ cls: s.className, ...box(s) })) };
    })()`);
    await page.locator('#display-settings').screenshot({ path: path.join(outDir, `i7-report-sheet-${width}.png`) });

    // a scenario's settings, from the gear on its diagrams section's title
    await openScenario(page, 'Collect milk');
    await page.evaluate(() => { const g = document.querySelector('details.scenario[data-probe] .gear-in-summary'); if (!g) throw new Error('no .gear-in-summary'); g.click(); });
    await frames(page, 4);
    out.scenario = await page.evaluate(`(() => {
      const box = (${widths})();
      const sheet = document.querySelector('details.scenario[data-probe] .diagram-options-bar');
      return { sheet: box(sheet), alignItems: getComputedStyle(sheet).alignItems,
        zones: Array.from(sheet.children).map((z) => ({ cls: z.className, ...box(z) })),
        selects: Array.from(sheet.querySelectorAll('select')).map((s) => ({ cls: s.className, ...box(s) })) };
    })()`);
    await page.locator('details.scenario[data-probe] .diagram-options-bar').screenshot({ path: path.join(outDir, `i7-scenario-sheet-${width}.png`) });

    // the Timeline tab's controls
    await page.evaluate(() => {
      const tab = Array.from(document.querySelectorAll('.nav-row [role="tab"], .nav-row button, .nav-row a')).find((t) => /timeline/i.test(t.textContent));
      if (!tab) throw new Error('no Timeline tab'); tab.click();
    });
    await page.waitForFunction(() => { const c = document.querySelector('#scenario-timeline .timeline-controls'); return c && c.getBoundingClientRect().width > 0; }, null, { polling: 200, timeout: 30000 });
    await frames(page, 4);
    out.timeline = await page.evaluate(`(() => {
      const box = (${widths})();
      const c = document.querySelector('#scenario-timeline .timeline-controls');
      return { controls: box(c), card: box(document.querySelector('#scenario-timeline')),
        groups: Array.from(c.querySelectorAll('.tl-seg')).map((g) => ({ label: g.getAttribute('aria-label'), ...box(g),
          buttons: Array.from(g.querySelectorAll('.tl-seg-btn')).map((b) => ({ text: b.textContent, ...box(b), lines: (() => { const cs = getComputedStyle(b); const inner = b.getBoundingClientRect().height - parseFloat(cs.paddingTop) - parseFloat(cs.paddingBottom) - parseFloat(cs.borderTopWidth) - parseFloat(cs.borderBottomWidth); return Math.round(inner / parseFloat(cs.lineHeight || '16')); })() })) })) };
    })()`);
    await page.locator('#scenario-timeline .timeline-header').screenshot({ path: path.join(outDir, `i7-timeline-${width}.png`) });
    write(path.join(outDir, `i7-phone-${width}.json`), out);
    console.log(JSON.stringify(out, null, 2));
  } finally {
    await browser.close();
  }
})().catch((e) => { console.error(e); process.exit(1); });
