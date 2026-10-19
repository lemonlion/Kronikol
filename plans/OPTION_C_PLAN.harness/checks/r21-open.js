// Round 17 item 21 (owner's bug): Firefox, component diagram, right-click menu, open the image in a new tab fails.
// Drives the page's own context menu on a diagram and, per "open ... in new tab" item, reports what the new tab got
// (its URL kind, what it shows) and every error and console message in the opener.
// usage: node r21-open.js <page> <chromium|firefox> <component|scenario text>
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require('../../../tools/playwright/installed-browser.js');
const path = require('path');
(async () => {
  const [page, browserName, which] = process.argv.slice(2);
  const b = await launchInstalled(pw[browserName], browserName === 'chromium' ? { channel: 'chrome' } : { executablePath: 'C:/Users/cex/AppData/Local/ms-playwright/firefox-1511/firefox/firefox.exe', firefoxUserPrefs: process.env.BLOCK ? { 'dom.disable_open_during_load': true, 'dom.popup_maximum': 20, 'privacy.popups.disable_from_plugins': 2 } : {} });
  const ctx = await b.newContext(browserName === 'firefox' ? { viewport: null } : { viewport: { width: 1400, height: 900 } });
  await ctx.addInitScript(() => { try { localStorage.setItem('kronikol.theme', 'light'); localStorage.removeItem('kronikol.reportSettings'); } catch (e) {} });
  const p = await ctx.newPage();
  const logs = [];
  p.on('pageerror', e => logs.push('pageerror: ' + e.message.slice(0, 200)));
  p.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') logs.push('console.' + m.type() + ': ' + m.text().slice(0, 200)); });
  await p.goto('file:///' + path.resolve(page).split(path.sep).join('/')); await p.waitForTimeout(3000);
  const ok = await p.evaluate(async which => {
    let host;
    if (which === 'component') {
      const sec = document.querySelector('.component-diagram-section'); if (!sec) return 'no component section';
      let n = sec; while (n) { if (n.tagName === 'DETAILS') n.open = true; n = n.parentElement; }
      const tab = Array.from(document.querySelectorAll('button, [role=tab], a')).find(t => /^components?$/i.test(t.textContent.trim())); if (tab) tab.click();
      host = sec;
    } else {
      const s = Array.from(document.querySelectorAll('details.scenario')).find(x => x.querySelector(':scope > summary').textContent.toLowerCase().includes(which));
      s.closest('details.feature').open = true; s.open = true; host = s.querySelector('.diagram-view-seq');
    }
    host.scrollIntoView();
    for (let i = 0; i < 60 && !host.querySelector('.plantuml-browser svg'); i++) await new Promise(r => setTimeout(r, 250));
    const svg = host.querySelector('.plantuml-browser svg'); if (!svg) return 'no svg';
    svg.setAttribute('data-probe-svg', '1');
    return 'svg ' + Math.round(svg.getBoundingClientRect().width) + 'x' + Math.round(svg.getBoundingClientRect().height);
  }, which);
  console.log('target', ok);
  const items = ['Open as PNG image in new tab', 'Open as PNG image (no transparency) in new tab', 'Open as SVG image in new tab'];
  for (const label of items) {
    logs.length = 0;
    const found = await p.evaluate(label => {
      const svg = document.querySelector('[data-probe-svg]'); const r = svg.getBoundingClientRect();
      svg.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: r.left + 30, clientY: r.top + 30 }));
      const it = Array.from(document.querySelectorAll('.diagram-ctx-menu div')).find(e => e.textContent.trim() === label && e.children.length === 0);
      if (!it) return false;
      it.setAttribute('data-probe-item', '1');
      return true;
    }, label);
    if (!found) { console.log('== ' + label + ': menu item not found'); continue; }
    const popupP = ctx.waitForEvent('page', { timeout: 8000 }).catch(() => null);
    await p.evaluate(() => { const it = document.querySelector('[data-probe-item]'); it.click(); it.removeAttribute('data-probe-item'); });
    const pop = await popupP;
    let res = 'no new tab';
    if (pop) {
      await pop.waitForLoadState('load', { timeout: 8000 }).catch(e => { res = 'load: ' + e.message.slice(0, 80); });
      await pop.waitForTimeout(800);
      const u = pop.url();
      const what = await pop.evaluate(() => { const img = document.querySelector('img'); const svg = document.querySelector('svg'); return (img ? 'img ' + img.naturalWidth + 'x' + img.naturalHeight : '') + (svg ? ' svg root ' + (document.documentElement.tagName) : '') + ' body text "' + (document.body ? document.body.innerText.slice(0, 80) : '') + '"'; }).catch(e => 'eval failed: ' + e.message.slice(0, 120));
      res = 'url ' + u.slice(0, 40) + ' | ' + what;
      await pop.close();
    }
    console.log('== ' + label + ': ' + res);
    logs.forEach(l => console.log('   ' + l));
  }
  await b.close();
})();
