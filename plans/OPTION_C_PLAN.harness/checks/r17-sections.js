// Round 17 item 18: each scenario section heading (Failure Result, Background, Steps, Diagrams) - where its card,
// summary, marker and first letter sit - and a screenshot of each heading row for r17-sections.py.
// usage: node r17-sections.js <page> <chromium|firefox> <scenario text> <out prefix> [extra css file]
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const path = require('path');
const fs = require('fs');
(async () => {
  const [page, browserName, pick, out, cssFile] = process.argv.slice(2);
  const b = await pw[browserName].launch(browserName === 'chromium' ? { channel: 'chrome' } : { executablePath: 'C:/Users/cex/AppData/Local/ms-playwright/firefox-1511/firefox/firefox.exe' });
  const ctx = await b.newContext(browserName === 'firefox' ? { viewport: null } : { viewport: { width: 1400, height: 900 } });
  await ctx.addInitScript(() => { try { localStorage.setItem('kronikol.theme', 'light'); localStorage.removeItem('kronikol.reportSettings'); } catch (e) {} });
  const p = await ctx.newPage();
  await p.goto('file:///' + path.resolve(page).split(path.sep).join('/')); await p.waitForTimeout(3000);
  if (cssFile) await p.addStyleTag({ content: fs.readFileSync(cssFile, 'utf8') });
  const sels = ['.failure-result', '.scenario-background', '.scenario-steps', '.example-diagrams'];
  const found = await p.evaluate(([pick, sels]) => {
    const s = Array.from(document.querySelectorAll('details.scenario')).find(x => x.querySelector(':scope > summary').textContent.toLowerCase().includes(pick));
    s.closest('details.feature').open = true; s.open = true; s.setAttribute('data-probe', '1');
    return sels.map(sel => { const d = s.querySelector(sel); if (!d) return null; d.setAttribute('data-sec', sel.slice(1)); return sel; }).filter(Boolean);
  }, [pick, sels]);
  const res = [];
  for (const sel of found) {
    const name = sel.slice(1);
    const info = await p.evaluate(name => {
      const d = document.querySelector('[data-sec="' + name + '"]');
      const sum = d.querySelector(':scope > summary');
      sum.scrollIntoView({ block: 'center' });
      const o = d.getBoundingClientRect(), sb = sum.getBoundingClientRect();
      const walker = document.createTreeWalker(sum, NodeFilter.SHOW_TEXT);
      let tn = null; while (walker.nextNode()) { if (walker.currentNode.textContent.trim()) { tn = walker.currentNode; break; } }
      const off = tn.textContent.indexOf(tn.textContent.trim()[0]);
      const range = document.createRange(); range.setStart(tn, off); range.setEnd(tn, off + 1);
      const t = range.getBoundingClientRect();
      const cs = getComputedStyle(sum), bs = getComputedStyle(sum, '::before');
      return { name, card: { x: o.left, y: o.top }, sum: { x: sb.left - o.left, y: sb.top - o.top, w: sb.width, h: sb.height, pl: cs.paddingLeft, list: cs.listStyleType, color: cs.color },
        before: bs.content, first: { ch: tn.textContent.trim()[0], x: t.left - o.left, y: t.top - o.top, h: t.height } };
    }, name);
    await p.waitForTimeout(250);
    // the summary's own box (positions above are from the card's left edge; the summary starts 1px in, past the border)
    await p.locator('[data-sec="' + name + '"] > summary').first().screenshot({ path: out + '-' + name + '.png' });
    res.push(info);
  }
  fs.writeFileSync(out + '.json', JSON.stringify(res));
  res.forEach(r => console.log(r.name, JSON.stringify(r.sum), r.before, JSON.stringify(r.first)));
  await b.close();
})();
