// node specs-check.js <Specifications.html>: which of the search's and the row switch's functions the Specifications page has.
const path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require(path.resolve(__dirname, '../../../../tools/playwright/installed-browser.js'));
(async () => {
    const browser = await launchInstalled(chromium, {});
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    await page.goto('file:///' + path.resolve(process.argv[2]).split(path.sep).join('/'));
    await page.waitForTimeout(1500);
    console.log(await page.evaluate(() => JSON.stringify({ fc: typeof fc, selectRow: typeof selectRow, reveal: typeof revealMatchSections, run: typeof run_search_scenarios, bar: !!document.getElementById('searchbar'), feature: document.querySelectorAll('details.feature').length, scenario: document.querySelectorAll('details.scenario').length, panels: document.querySelectorAll('.param-detail-panel').length })));
    console.log('page errors: ' + errors.length);
    await browser.close();
})();
