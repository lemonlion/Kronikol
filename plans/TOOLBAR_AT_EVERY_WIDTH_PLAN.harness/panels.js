// The panels the top bar opens (Scenario Timeline, Component Diagram) and the sections outside any feature
// (failure clusters, history, diagnostics) at every width: does the page scroll sideways, and does anything
// run past the viewport's content edge? Usage: node panels.js <page.html,...> [scrollbars=0|1] [step]
const pw = require(require('path').resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const path = require('path');
const [, , filesArg, sb, stepArg] = process.argv;
const step = +(stepArg || 20);
const TS = process.env.SPACING === '1'
  ? '*{line-height:1.5 !important;letter-spacing:0.12em !important;word-spacing:0.16em !important}p{margin-bottom:2em !important}' : '';
(async () => {
  const browser = await pw[process.env.ENGINE || 'chromium'].launch(sb === '1' && !process.env.ENGINE ? { ignoreDefaultArgs: ['--hide-scrollbars'] } : {});
  for (const file of filesArg.split(',')) {
    const ctx = await browser.newContext({ viewport: { width: 320, height: 900 } });
    await ctx.route(/^https?:/, r => r.abort());
    const page = await ctx.newPage();
    const bad = [];
    for (let w = 320; w <= 1400; w += step) {
      await page.setViewportSize({ width: w, height: 900 });
      await page.goto('file:///' + path.resolve(file).replace(/\\/g, '/'), { waitUntil: 'domcontentloaded', timeout: 120000 });
      await page.waitForSelector('details.feature', { state: 'attached' });
      if (TS) await page.addStyleTag({ content: TS });
      if (process.env.INJECT) await page.addStyleTag({ content: process.env.INJECT });
      for (const panel of ['scenario-timeline', 'component-diagram']) {
        const r = await page.evaluate(p => {
          document.querySelectorAll('details').forEach(d => d.open = true); document.querySelectorAll('.search-help-panel').forEach(x => x.style.display = ''); const fl = document.querySelector('.filters'); if (fl && getComputedStyle(fl).display === 'none') fl.style.display = 'flex';
          const tl = document.getElementById('scenario-timeline'), cd = document.getElementById('component-diagram');
          if (tl) tl.style.display = p === 'scenario-timeline' ? '' : 'none';
          if (cd) cd.style.display = p === 'component-diagram' ? '' : 'none';
          const el = document.getElementById(p);
          if (!el) return null;
          const de = document.documentElement, right = de.clientWidth;
          const past = [];
          for (const sel of ['#' + p, '.failure-clusters', '.history-section', '.report-diagnostics', '.background-calls', '.features-summary-details', '.toolbar-row', '.header-row']) {
            const host = document.querySelector(sel);
            if (!host || host.getBoundingClientRect().width === 0) continue;
            const walker = document.createTreeWalker(host, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT);
            const range = document.createRange();
            let worst = null, worstOver = 1;
            for (let n = walker.currentNode; n; n = walker.nextNode()) {
              const text = n.nodeType === 3;
              if (text && !n.data.trim()) continue;
              const e = text ? n.parentElement : n;
              if (e.closest('svg') && (text || e.localName !== 'svg')) continue;
              let rect; if (text) { range.selectNodeContents(n); rect = range.getBoundingClientRect(); } else rect = e.getBoundingClientRect();
              if (rect.width === 0) continue;
              let a = text ? e : e.parentElement, scrolled = false;
              for (; a && a !== host.parentElement && !scrolled; a = a.parentElement) scrolled = a !== e && /^(auto|scroll|hidden)$/.test(getComputedStyle(a).overflowX);
              if (scrolled) continue;
              const over = rect.right - right;
              if (over > worstOver) { worst = (text ? '"' + n.data.trim().slice(0, 30) + '"' : e.localName + '.' + [...e.classList].join('.')); worstOver = over; }
            }
            if (worst) past.push(`${sel}: ${worst} +${Math.round(worstOver)}`);
          }
          return { scroll: de.scrollWidth - de.clientWidth, past };
        }, panel);
        if (r && (r.scroll > 1 || r.past.length)) bad.push(`${w} ${panel}: scroll +${r.scroll}; ${r.past.join('; ')}`);
      }
    }
    console.log(`== ${path.basename(file)}${TS ? ' (text spacing)' : ''}${sb === '1' ? ' (scrollbar)' : ''}: ${bad.length ? bad.length + ' bad' : 'clean'}`);
    bad.filter(b => !b.includes("component-diagram")).forEach(b => console.log('  ' + b));
    await ctx.close();
  }
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
