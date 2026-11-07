// How the engine draws a call label's words in the object-words fixture: one text per word, or the label whole.
// Bundled Chromium only (never a channel: CLAUDE.md).
const { pathToFileURL } = require('url');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
  await page.goto(pathToFileURL(process.argv[2]).href);
  await page.waitForSelector('details.feature');
  await page.evaluate(() => {
    document.querySelectorAll('details.feature').forEach((f) => { f.open = true; });
    document.querySelectorAll('details.scenario').forEach((s) => { s.open = true; if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(s); });
  });
  await page.waitForFunction(() => document.querySelectorAll('details.scenario')[3].querySelector('[data-diagram-type="plantuml"] > svg'), null, { timeout: 120000, polling: 200 });
  const r = await page.evaluate(() => {
    const svg = document.querySelectorAll('details.scenario')[3].querySelector('[data-diagram-type="plantuml"] > svg');
    const texts = Array.from(svg.querySelectorAll('text')).map((t) => t.textContent);
    return { count: texts.length, exact: texts.filter((t) => t === 'constructor').length,
      containing: texts.filter((t) => t.includes('constructor')).slice(0, 4), sample: texts.slice(0, 40) };
  });
  console.log(JSON.stringify(r, null, 1));
  await browser.close();
})();
