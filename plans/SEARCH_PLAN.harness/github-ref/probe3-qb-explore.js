// Probe 3: open the search overlay on the logged-out results page and dump its DOM. One page load.
const { launch, newContext, save, installMeasure, sleep, OUT } = require('./lib');
const path = require('path');

(async () => {
  const browser = await launch();
  const ctx = await newContext(browser, { width: 1280, height: 900 });
  const page = await ctx.newPage();
  const reqs = [];
  page.on('request', (r) => { if (/github\.com\/(search|_graphql|.*suggest|.*jump)/.test(r.url()) && r.resourceType() !== 'document') reqs.push(r.method() + ' ' + r.url()); });
  await page.goto('https://github.com/search?q=playwright&type=repositories', { waitUntil: 'load', timeout: 60000 });
  await sleep(3000);
  await installMeasure(page);
  // Focus the body and press "/".
  await page.mouse.click(640, 860);
  await page.keyboard.press('/');
  await sleep(2000);
  const dump = await page.evaluate(() => {
    const out = {};
    out.active = document.activeElement ? document.activeElement.outerHTML.slice(0, 2000) : null;
    out.dialogs = Array.from(document.querySelectorAll('dialog, [role="dialog"], [role="combobox"], [role="listbox"]')).map(d => ({
      tag: d.tagName, role: d.getAttribute('role'), open: d.open, cls: d.className, id: d.id,
      rect: d.getBoundingClientRect().toJSON(), len: d.outerHTML.length,
    }));
    return out;
  });
  console.log(JSON.stringify(dump, null, 2));
  const dlgHtml = await page.evaluate(() => {
    const ds = Array.from(document.querySelectorAll('dialog[open], [role="dialog"]'));
    return ds.map(d => d.outerHTML).join('\n\n<!-- ======== -->\n\n');
  });
  save('explore-qb-open-empty.html', dlgHtml);
  await page.screenshot({ path: path.join(OUT, 'explore-qb-open-1280.png') });
  await page.keyboard.type('lan', { delay: 120 });
  await sleep(2000);
  save('explore-qb-lan.html', await page.evaluate(() => Array.from(document.querySelectorAll('dialog[open], [role="dialog"]')).map(d => d.outerHTML).join('\n\n<!-- ======== -->\n\n')));
  await page.screenshot({ path: path.join(OUT, 'explore-qb-lan-1280.png') });
  console.log('requests:', JSON.stringify(reqs, null, 1));
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
