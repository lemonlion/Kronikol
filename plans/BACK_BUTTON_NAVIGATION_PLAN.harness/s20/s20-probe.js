// S20: does an inline onclick that calls event.preventDefault() stop Ctrl+click, Shift+click and a middle
// click from opening the link in a new tab or window? Bundled browsers only (launchInstalled falls through to
// launch() for them), headless, file:// page, each case on a fresh context.
const fs = require('fs');
const path = require('path');
const { pathToFileURL } = require('url');
const pw = require('C:/Users/cex/.nuget/packages/microsoft.playwright/1.59.0/.playwright/package/index.js');
const { launchInstalled } = require('C:/Code/Kronikol/tools/playwright/installed-browser.js');

const dir = __dirname;
const page = path.join(dir, 'page.html');
fs.writeFileSync(page, `<!doctype html><meta charset="utf-8"><title>s20</title>
<p><a id="cancelled" href="#sid-0123456789abcdef" onclick="event.preventDefault();history.replaceState(null,'',location.pathname+'#sid-0123456789abcdef');">cancelled</a></p>
<p><a id="plain" href="#sid-0123456789abcdef">plain</a></p>`);
const url = pathToFileURL(page).href;

(async () => {
  const rows = [];
  for (const name of ['chromium', 'firefox', 'webkit']) {
    let browser;
    try { browser = await launchInstalled(pw[name], { headless: true }); }
    catch (e) { rows.push({ engine: name, error: String(e).split('\n')[0] }); continue; }
    const version = browser.version();
    for (const link of ['cancelled', 'plain']) {
      for (const how of ['ctrl', 'shift', 'middle']) {
        const context = await browser.newContext();
        const p = await context.newPage();
        await p.goto(url);
        const opened = [];
        context.on('page', np => opened.push(np));
        let err = null;
        try {
          if (how === 'ctrl') await p.click('#' + link, { modifiers: ['ControlOrMeta'] });
          else if (how === 'shift') await p.click('#' + link, { modifiers: ['Shift'] });
          else await p.click('#' + link, { button: 'middle' });
        } catch (e) { err = String(e).split('\n')[0]; }
        await p.waitForTimeout(1500);
        rows.push({ engine: name, version, link, how, newPages: opened.length, urls: opened.map(o => o.url()), hashAfter: await p.evaluate(() => location.hash), err });
        await context.close();
      }
    }
    await browser.close();
  }
  fs.writeFileSync(path.join(dir, 's20-results.json'), JSON.stringify(rows, null, 2));
  for (const r of rows) console.log(JSON.stringify(r));
})().catch(e => { console.error(e); process.exit(1); });
