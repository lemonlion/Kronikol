// Probe the live demo report's title line, filter box and bar at several widths, light and dark (bundled Chromium only).
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const fs = require('fs'), path = require('path');
const out = __dirname;
const url = process.argv[2] || 'https://lemonlion.github.io/BreakfastProvider/reports/reqnroll/TestRunReport.html';
(async () => {
  const browser = await pw.chromium.launch();
  const results = {};
  for (const theme of ['light', 'dark']) {
    for (const w of [1440, 1280, 1012, 768, 390]) {
      const ctx = await browser.newContext({ viewport: { width: w, height: 900 }, colorScheme: theme });
      const page = await ctx.newPage();
      await page.addInitScript(t => { try { localStorage.setItem('kronikol.theme', t); } catch (e) {} }, theme);
      await page.goto(url, { waitUntil: 'load', timeout: 120000 });
      await page.waitForTimeout(2500);
      const m = await page.evaluate(() => {
        const box = el => { if (!el) return null; const r = el.getBoundingClientRect(); const cs = getComputedStyle(el); return { x: Math.round(r.x*10)/10, y: Math.round(r.y*10)/10, w: Math.round(r.width*10)/10, h: Math.round(r.height*10)/10, display: cs.display, visible: !!el.getClientRects().length && cs.visibility !== 'hidden' }; };
        const q = s => document.querySelector(s);
        const tt = q('.theme-toggle');
        const titleLine = tt && tt.parentElement;
        return {
          version: (document.querySelector('meta[name=generator]') || {}).content || null,
          title: document.title,
          themeAttr: document.documentElement.getAttribute('data-theme'),
          themeToggle: box(tt), themeToggleStyle: tt && (() => { const cs = getComputedStyle(tt); return { w: cs.width, h: cs.height, radius: cs.borderRadius, border: cs.border, bg: cs.backgroundColor, color: cs.color, padding: cs.padding }; })(),
          titleLineClass: titleLine && titleLine.className, titleLineTag: titleLine && titleLine.tagName,
          titleLine: box(titleLine),
          titleLineChildren: titleLine ? Array.from(titleLine.children).map(c => ({ tag: c.tagName, cls: c.className && c.className.baseVal === undefined ? c.className : (c.className.baseVal || ''), text: (c.textContent || '').trim().slice(0, 60), box: box(c) })) : null,
          runBand: box(q('.run-band')),
          filteringBox: box(q('.filtering-box')), filteringBoxClass: q('.filtering-box') && q('.filtering-box').className,
          searchbar: box(q('#searchbar')),
          moreFilters: box(q('.more-filters')),
          filterRows: box(q('#filter-rows')),
          nav: box(q('.toolbar-row.report-nav')),
          navFiltersToggle: box(q('.nav-filters-toggle')),
          navReadout: box(q('.nav-readout')),
          tabs: box(q('.report-tabs')),
          mobileToggle: box(q('.mobile-filter-toggle')),
          scenarios: document.querySelectorAll('[data-scenario-id], .scenario').length,
          features: document.querySelectorAll('.feature').length,
          docH: document.documentElement.scrollHeight,
        };
      });
      results[`${theme}-${w}`] = m;
      await page.screenshot({ path: path.join(out, `top-${w}-${theme}.png`) });
      if (w === 1280) {
        await page.evaluate(() => window.scrollTo(0, 2500));
        await page.waitForTimeout(600);
        const pinned = await page.evaluate(() => { const t = document.querySelector('.nav-filters-toggle'); const r = t && t.getBoundingClientRect(); const n = document.querySelector('.toolbar-row.report-nav'); return { toggle: t && { w: r.width, h: r.height, x: r.x, y: r.y, visible: !!t.getClientRects().length }, navClass: n && n.className }; });
        results[`${theme}-${w}-scrolled`] = pinned;
        await page.screenshot({ path: path.join(out, `scrolled-${w}-${theme}.png`) });
      }
      await ctx.close();
    }
  }
  fs.writeFileSync(path.join(out, 'probe.json'), JSON.stringify(results, null, 1));
  await browser.close();
  console.log(JSON.stringify(results['light-1280'], null, 1));
  console.log(JSON.stringify(results['light-1280-scrolled'], null, 1));
})().catch(e => { console.error(e); process.exit(1); });
