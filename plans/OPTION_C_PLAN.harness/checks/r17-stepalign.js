// Round 17 item 18: where the Steps summary's marker, its 'S', each top-level step's marker and each step's status
// circle sit, in layout (boxes) and in paint (an element screenshot read by r17-stepalign.py).
// usage: node r17-stepalign.js <page> <chromium|firefox> <scenario text> <out prefix> [extra css file]
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require('../../../tools/playwright/installed-browser.js');
const path = require('path');
const fs = require('fs');
(async () => {
  const [page, browserName, pick, out, cssFile] = process.argv.slice(2);
  const b = await launchInstalled(pw[browserName], browserName === 'chromium' ? { channel: 'chrome' } : { executablePath: 'C:/Users/cex/AppData/Local/ms-playwright/firefox-1511/firefox/firefox.exe' });
  const ctx = await b.newContext(browserName === 'firefox' ? { viewport: null } : { viewport: { width: 1400, height: 900 } });
  await ctx.addInitScript(() => { try { localStorage.setItem('kronikol.theme', 'light'); localStorage.removeItem('kronikol.reportSettings'); } catch (e) {} });
  const p = await ctx.newPage();
  await p.goto('file:///' + path.resolve(page).split(path.sep).join('/')); await p.waitForTimeout(3000);
  if (cssFile) await p.addStyleTag({ content: fs.readFileSync(cssFile, 'utf8') });
  const info = await p.evaluate(pick => {
    const s = Array.from(document.querySelectorAll('details.scenario')).find(x => x.querySelector(':scope > summary').textContent.toLowerCase().includes(pick));
    s.closest('details.feature').open = true; s.open = true; s.setAttribute('data-probe', '1');
    const steps = s.querySelector('.scenario-steps');
    steps.open = true;
    steps.querySelectorAll(':scope > .step-collapsible').forEach(d => d.open = false);
    steps.scrollIntoView({ block: 'center' });
    const o = steps.getBoundingClientRect();
    const sum = steps.querySelector(':scope > summary');
    const range = document.createRange();
    const walker = document.createTreeWalker(sum, NodeFilter.SHOW_TEXT);
    let tn = null; while (walker.nextNode()) { if (walker.currentNode.textContent.trim()) { tn = walker.currentNode; break; } }
    const off = tn.textContent.indexOf(tn.textContent.trim()[0]);
    range.setStart(tn, off); range.setEnd(tn, off + 1);
    const S = range.getBoundingClientRect();
    const sb = sum.getBoundingClientRect();
    const rows = Array.from(steps.querySelectorAll(':scope > .step, :scope > .step-collapsible')).filter(e => e.getBoundingClientRect().height > 0).map(e => {
      const head = e.matches('details') ? e.querySelector(':scope > summary') : e;
      const hb = head.getBoundingClientRect();
      const st = head.querySelector('.step-status');
      const cb = st ? st.getBoundingClientRect() : null;
      const bs = getComputedStyle(head, '::before');
      return { kind: e.matches('details') ? 'details' : 'div', cls: e.className, top: hb.top - o.top, h: hb.height, left: hb.left - o.left,
        before: bs.content !== 'none' ? { left: bs.left, top: bs.top, w: bs.width, content: bs.content } : null,
        circle: cb ? { x: cb.left - o.left, y: cb.top - o.top, w: cb.width, h: cb.height } : null,
        text: head.textContent.trim().replace(/\s+/g, ' ').slice(0, 40) };
    });
    const sbs = getComputedStyle(sum, '::before'), sm = getComputedStyle(sum, '::marker');
    return { box: { w: o.width, h: o.height }, summary: { top: sb.top - o.top, h: sb.height, left: sb.left - o.left, pl: getComputedStyle(sum).paddingLeft, display: getComputedStyle(sum).display, listStyle: getComputedStyle(sum).listStyleType,
      before: sbs.content, beforeLeft: sbs.left, marker: sm.content }, S: { x: S.left - o.left, y: S.top - o.top, w: S.width, h: S.height }, rows };
  }, pick);
  console.log(JSON.stringify(info, null, 1));
  fs.writeFileSync(out + '.json', JSON.stringify(info));
  await p.waitForTimeout(400);
  await p.locator('[data-probe] .scenario-steps').first().screenshot({ path: out + '.png' });
  await b.close();
})();
