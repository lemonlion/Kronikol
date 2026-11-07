'use strict';
// Where the report's diagram toggles are at a window width: each copy, whether it shows, and the first ancestor hiding it.
// usage: node narrow.js <fixture-dir> <vw> [vh]
const fs = require('fs'), path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
(async () => {
  const [dir, vw, vh = '768'] = process.argv.slice(2);
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: +vw, height: +vh } });
  await page.goto(require('url').pathToFileURL(path.resolve(dir, fs.readFileSync(path.join(dir, 'ENTRY.txt'), 'utf8').trim())).href);
  await page.waitForSelector('details.feature', { timeout: 60000 });
  await page.waitForTimeout(1500);
  console.log(JSON.stringify(await page.evaluate(() => Array.from(document.querySelectorAll("button[data-toggle='headers']")).map((b) => {
    let hid = null;
    for (let n = b; n && n !== document.body; n = n.parentElement) {
      const cs = getComputedStyle(n);
      if (cs.display === 'none' || cs.visibility === 'hidden' || (n.tagName === 'DETAILS' && !n.open && n !== b)) { hid = n.tagName + '.' + n.className + (n.id ? '#' + n.id : ''); break; }
    }
    const r = b.getBoundingClientRect();
    return { onclick: b.getAttribute('onclick'), w: Math.round(r.width), h: Math.round(r.height), hiddenBy: hid,
      inBar: !!b.closest('.toolbar-row.report-nav'), path: [b.parentElement, b.parentElement && b.parentElement.parentElement].filter(Boolean).map((n) => n.tagName + '.' + n.className).join(' < ') };
  })), null, 0));
  await browser.close();
})();
