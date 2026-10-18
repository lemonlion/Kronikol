// Items 8 and 12: CSS variants x sub-pixel offsets of the bars, in one browser. For each, screenshots of the report
// radio, scenario radio and scenario flags with their rects, for r17-align.py.
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const path = require('path'), fs = require('fs');
const VARIANTS = JSON.parse(fs.readFileSync(process.argv[5] || 'r17-variants.json', 'utf8'));
(async () => {
  const [page, browserName, out] = process.argv.slice(2);
  const b = await pw[browserName].launch(browserName === 'chromium' ? { channel: 'chrome' } : { executablePath: 'C:/Users/cex/AppData/Local/ms-playwright/firefox-1511/firefox/firefox.exe' });
  const ctx = await b.newContext(browserName === 'firefox' ? { viewport: null } : { viewport: { width: 1400, height: 900 } });
  await ctx.addInitScript(() => { try { localStorage.setItem('kronikol.theme', 'light'); localStorage.removeItem('kronikol.reportSettings'); } catch (e) {} });
  const p = await ctx.newPage();
  await p.goto('file:///' + path.resolve(page).split(path.sep).join('/')); await p.waitForTimeout(3000);
  await p.evaluate(() => {
    const s = Array.from(document.querySelectorAll('details.scenario')).find(x => x.querySelector('.toggle-btn[data-toggle="events"]'));
    s.closest('details.feature').open = true; s.open = true; s.querySelectorAll('details').forEach(d => d.open = true); s.setAttribute('data-probe', '1');
    const st = document.createElement('style'); st.id = 'k-var'; document.head.appendChild(st);
    const sp = document.createElement('div'); sp.id = 'k-spacer'; document.body.insertBefore(sp, document.body.firstChild);
    document.querySelector('.toolbar-right .details-radio').setAttribute('data-align', 'report-radio');
    const sb = document.querySelector('[data-probe] .diagram-options-bar');
    sb.querySelector('.details-radio').setAttribute('data-align', 'scenario-radio');
    sb.querySelector('.diagram-flags').setAttribute('data-align', 'scenario-flags');
    document.querySelector('.toolbar-right .diagram-flags').setAttribute('data-align', 'report-flags');
  });
  await p.waitForTimeout(2500);
  for (const [v, css] of Object.entries(VARIANTS)) {
    for (const f of (process.env.FRACS || '0,0.25,0.5,0.75').split(',').map(Number)) {
      await p.evaluate(([css, f]) => { document.getElementById('k-var').textContent = css; document.getElementById('k-spacer').style.height = f + 'px'; }, [css, f]);
      await p.waitForTimeout(150);
      const dir = path.join(out, v + '-' + f); fs.mkdirSync(dir, { recursive: true });
      const meta = {};
      for (const name of (process.env.BARS || 'report-radio,scenario-radio,scenario-flags').split(',')) {
        const loc = p.locator('[data-align="' + name + '"]').first();
        await loc.scrollIntoViewIfNeeded();
        await loc.screenshot({ path: path.join(dir, name + '.png') });
        meta[name] = await loc.evaluate(el => { const o = el.getBoundingClientRect(); return Array.from(el.querySelectorAll('.details-radio-label, .details-radio-btn, .toggle-btn')).slice(0, 3).map(c => { const r = c.getBoundingClientRect(); return { cls: c.className, label: c.textContent.trim().slice(0, 20), x: r.left - o.left, y: r.top - o.top, w: r.width, h: r.height, pl: parseFloat(getComputedStyle(c).paddingLeft) }; }); });
      }
      fs.writeFileSync(path.join(dir, 'meta.json'), JSON.stringify({ dpr: 1, meta }));
    }
  }
  console.log(browserName, 'done');
  await b.close();
})();
