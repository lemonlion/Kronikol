// Round 17 item 19: each diagram kind's drawing - its container chain, the drawn element's box against its container,
// display and margins - so centring can be checked (and a wider-than-box drawing still scrolls from its left edge).
// usage: node r19-layout.js <page> <chromium|firefox> <scenario text> [css file]
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const path = require('path');
const fs = require('fs');
(async () => {
  const [page, browserName, pick, cssFile] = process.argv.slice(2);
  const b = await pw[browserName].launch(browserName === 'chromium' ? { channel: 'chrome' } : { executablePath: 'C:/Users/cex/AppData/Local/ms-playwright/firefox-1511/firefox/firefox.exe' });
  const ctx = await b.newContext(browserName === 'firefox' ? { viewport: null } : { viewport: { width: 1400, height: 900 } });
  await ctx.addInitScript(() => { try { localStorage.setItem('kronikol.theme', 'light'); localStorage.removeItem('kronikol.reportSettings'); } catch (e) {} });
  const p = await ctx.newPage();
  await p.goto('file:///' + path.resolve(page).split(path.sep).join('/')); await p.waitForTimeout(3000);
  if (cssFile) await p.addStyleTag({ content: fs.readFileSync(cssFile, 'utf8') });
  await p.evaluate(pick => {
    const s = Array.from(document.querySelectorAll('details.scenario')).find(x => x.querySelector(':scope > summary').textContent.toLowerCase().includes(pick));
    s.closest('details.feature').open = true; s.open = true; s.setAttribute('data-probe', '1');
    s.scrollIntoView();
  }, pick);
  await p.waitForTimeout(2500);
  const tabs = ['Sequence', 'Activity', 'Flame'];
  for (const tab of tabs) {
    const r = await p.evaluate(async tab => {
      const s = document.querySelector('[data-probe]');
      const t = Array.from(s.querySelectorAll('button, [role=tab], a')).find(x => x.textContent.trim().startsWith(tab));
      if (t) t.click();
      await new Promise(res => setTimeout(res, 2500));
      const views = Array.from(s.querySelectorAll('.diagram-view')).filter(v => v.getBoundingClientRect().height > 0);
      return views.map(v => {
        const vb = v.getBoundingClientRect();
        const drawn = Array.from(v.querySelectorAll('svg, canvas, .flame-chart, [class*=flame-]')).filter(e => e.getBoundingClientRect().width > 50)[0];
        if (!drawn) return v.className + ' (nothing drawn)';
        const db = drawn.getBoundingClientRect();
        const chain = []; for (let n = drawn.parentElement; n && n !== v.parentElement; n = n.parentElement) { const cs = getComputedStyle(n); chain.push(n.tagName.toLowerCase() + '.' + String(n.className).split(/\s+/)[0] + '[' + cs.display + ' ta=' + cs.textAlign + ' ox=' + cs.overflowX + ' w=' + Math.round(n.getBoundingClientRect().width) + ']'); }
        const cs = getComputedStyle(drawn);
        return tab + ': ' + drawn.tagName.toLowerCase() + '.' + String(drawn.className.baseVal !== undefined ? drawn.className.baseVal : drawn.className).slice(0, 30) + ' display ' + cs.display + ' margin ' + cs.marginLeft + '/' + cs.marginRight + ' x ' + Math.round(db.left - vb.left) + ' w ' + Math.round(db.width) + ' of view w ' + Math.round(vb.width) + ' (right gap ' + Math.round(vb.right - db.right) + ')\n     chain ' + chain.join(' < ');
      }).join('\n  ');
    }, tab);
    console.log(r);
  }
  await b.close();
})();
