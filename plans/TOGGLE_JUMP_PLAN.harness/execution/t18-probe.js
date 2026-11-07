// T18's deep case outside the test: what the keeper took (its debug hook window.__kpPlace, a temporary line in the
// worktree's context-menu-script.js) and what it found after Databases. Bundled Chromium only (CLAUDE.md).
// node t18-probe.js <fixture.html> <kit.js> [filter] [where]
const fs = require('fs');
const { pathToFileURL } = require('url');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const [file, kitFile, filter = 'databases', where = 'deep:300'] = process.argv.slice(2);
(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
  await page.goto(pathToFileURL(file).href);
  await page.waitForSelector('details.feature');
  await page.evaluate(() => {
    document.querySelectorAll('details.feature').forEach((f) => { f.open = true; });
    document.querySelectorAll('details.scenario').forEach((s) => { s.open = true; if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(s); });
  });
  await page.waitForFunction(() => {
    const boxes = Array.from(document.querySelectorAll('details.scenario [data-diagram-type="plantuml"]'));
    return boxes.length && !window._plantumlRendering && boxes.every((c) => !!c.querySelector(':scope > svg'));
  }, null, { timeout: 180000, polling: 200 });
  await page.evaluate(`(${fs.readFileSync(kitFile, 'utf8')})()`);
  console.log('placed', await page.evaluate(([i, w]) => window.__k227.place(i, w), [8, where]));
  const before = await page.evaluate(() => {
    const c = document.querySelectorAll('details.scenario')[8].querySelector('[data-diagram-type="plantuml"]');
    const line = window.__k227.mark(8);
    return { line, texts: Array.from(c.querySelectorAll('svg text')).map((t) => { const r = t.getBoundingClientRect(); return [t.textContent, Math.round((r.top + r.bottom) / 2)]; })
      .filter((t) => Math.abs(t[1] - line.line) < 120) };
  });
  console.log('before', JSON.stringify(before));
  const b = await page.evaluate((f) => {
    const btn = Array.from(document.querySelectorAll(`button[data-toggle='${f}'][onclick^='window._toggle']:not([onclick*='Scenario'])`)).find((x) => x.offsetParent !== null);
    const r = btn.getBoundingClientRect();
    return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
  }, filter);
  await page.mouse.click(b.x, b.y);
  await page.waitForFunction(() => {
    if (window._plantumlRendering || document.querySelector('.details-pending')) return false;
    for (const c of document.querySelectorAll('[data-diagram-type="plantuml"]')) if (c._noteRendering || c._kQueued > 0 || c._kBatched > 0) return false;
    return !window.__mark || !document.contains(window.__mark.old);
  }, null, { timeout: 120000, polling: 200 });
  await page.waitForTimeout(600);
  const after = await page.evaluate(() => {
    const p = window.__kpPlace, out = JSON.parse(window.__k227.result());
    const c = document.querySelectorAll('details.scenario')[8].querySelector('[data-diagram-type="plantuml"]');
    const mid = (el) => { const r = el.getBoundingClientRect(); return Math.round((r.top + r.bottom) / 2); };
    return { moved: out.Moved, once: out.Once, onceMoved: out.OnceMoved, line: p && p.y,
      mark: p && p.mark && { words: p.mark.words, k: p.mark.k, off: Math.round(p.mark.off), unique: p.mark.unique.map((u) => [u.words, Math.round(u.off)]) },
      found: p && p.seen && p.seen.found && { text: p.seen.found.el.textContent, connected: p.seen.found.el.isConnected, y: mid(p.seen.found.el), off: Math.round(p.seen.found.off) },
      held: window.kronikol_readingPlace.held(),
      texts: Array.from(c.querySelectorAll('svg text')).map((t) => [t.textContent, mid(t)]).filter((t) => Math.abs(t[1] - (p ? p.y : 384)) < 120) };
  });
  console.log('after', JSON.stringify(after, null, 1));
  await browser.close();
})();
