// Does the bar refit its row (nav-compact / nav-minimal) when Filters appears as the bar floats? (bundled Chromium)
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const url = 'https://lemonlion.github.io/BreakfastProvider/reports/reqnroll/TestRunReport.html';
(async () => {
  const browser = await pw.chromium.launch();
  const out = [];
  for (const w of [780, 820, 860, 900, 960, 1012, 1100]) {
    const ctx = await browser.newContext({ viewport: { width: w, height: 900 } });
    const page = await ctx.newPage();
    await page.goto(url, { waitUntil: 'load', timeout: 120000 });
    await page.waitForTimeout(2000);
    const read = () => page.evaluate(() => {
      const row = document.querySelector('.nav-row'), tabs = document.querySelector('.report-tabs'), g = document.querySelector('.nav-filters'), t = document.querySelector('.nav-filters-toggle');
      return { cls: row.className, tabsOverflow: tabs.scrollWidth - tabs.clientWidth, rowW: row.clientWidth, need: tabs.scrollWidth + g.offsetWidth + 24, toggle: !!t.getClientRects().length };
    });
    const atRest = await read();
    await page.evaluate(() => window.scrollTo(0, 2500));
    await page.waitForTimeout(700);
    const floating = await read();
    // a resize to the same width makes fit() run: the state it should have had
    await page.setViewportSize({ width: w, height: 899 });
    await page.waitForTimeout(500);
    const refit = await read();
    out.push({ w, atRest, floating, refit });
    await ctx.close();
  }
  await browser.close();
  console.log(JSON.stringify(out, null, 0).replace(/\},\{"w"/g, '},\n{"w"'));
})().catch(e => { console.error(e); process.exit(1); });
