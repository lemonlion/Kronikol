// Items 29 + 30: the History tab's pass-rate rings (rate inside, arcs per outcome) and labelled duration bars.
// usage: node r29-30.js <page> <chromium|firefox> [shot.png] [dark]
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const path = require('path');
(async () => {
  const [page, browserName, shot, theme] = process.argv.slice(2);
  const b = await pw[browserName].launch(browserName === 'chromium' ? { channel: 'chrome' } : { executablePath: 'C:/Users/cex/AppData/Local/ms-playwright/firefox-1511/firefox/firefox.exe' });
  const ctx = await b.newContext(browserName === 'firefox' ? { viewport: null } : { viewport: { width: 1400, height: 900 } });
  await ctx.addInitScript(t => { try { localStorage.setItem('kronikol.theme', t || 'light'); } catch (e) {} }, theme || 'light');
  const p = await ctx.newPage(); const errors = []; p.on('pageerror', e => errors.push(String(e)));
  await p.goto('file:///' + path.resolve(page).split(path.sep).join('/')); await p.waitForTimeout(2500);
  const fails = []; const ok = (c, m) => { if (!c) fails.push(m); };
  const tab = p.locator('.report-nav [role="tab"], .report-nav button', { hasText: /^History/ }).first();
  await tab.click(); await p.waitForTimeout(1200);
  const r = await p.evaluate(() => {
    const rings = Array.from(document.querySelectorAll('.k-hist-ring'));
    const bars = Array.from(document.querySelectorAll('.k-hist-col'));
    const hidden = Array.from(document.querySelectorAll('svg.history-chart-svg')).map(s => s.getBoundingClientRect().height);
    return {
      rings: rings.map(x => ({ pct: x.querySelector('text').textContent, arcs: Array.from(x.querySelectorAll('circle')).map(c => c.getAttribute('class').replace('k-ring-', '')).join('+'), title: x.title.split('\u00b7').pop().trim(), w: Math.round(x.getBoundingClientRect().width) })),
      bars: bars.map(x => { const t = x.querySelector('.k-hist-time'); const tr = t.getBoundingClientRect(), br = x.querySelector('.k-hist-bar').getBoundingClientRect(); return { time: t.textContent, title: x.title.split('\u00b7').pop().trim(), inside: tr.top >= br.top - 0.5 && tr.bottom <= br.bottom + 0.5, h: Math.round(br.height) }; }),
      svgHeights: hidden,
    };
  });
  console.log(browserName, theme || 'light', JSON.stringify(r));
  ok(r.rings.length > 0 && r.rings.every(x => /^\d+%$/.test(x.pct) && x.w > 0), 'rings drawn with a rate');
  ok(r.rings.every(x => (x.title.indexOf(' 0 failed') >= 0) === (x.pct === '100%')), 'a run with a failure never reads 100%, a clean run does');
  ok(r.rings.every(x => (x.title.indexOf(' 0 failed') < 0) === (x.arcs.indexOf('fail') >= 0)), 'fail arc exactly where a run failed');
  ok(r.bars.length > 0 && r.bars.every(x => /^(\d+ms|\d+s|\d+m \d+s|\d+h \d+m)$/.test(x.time)), 'every bar has a time');
  ok(r.svgHeights.every(h => h === 0), 'the stretched svgs are hidden');
  const sec = p.locator('.history-charts').first();
  if (shot) { await sec.scrollIntoViewIfNeeded(); await sec.screenshot({ path: shot }); }
  ok(errors.length === 0, 'errors ' + errors.join(' | '));
  console.log(browserName, fails.length ? 'FAIL ' + fails.join('; ') : 'PASS');
  await b.close();
})();
