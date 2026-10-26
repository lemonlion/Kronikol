// Minimal check: is HTML in a <foreignObject> painted with the SVG's viewBox scale?
const fs = require('fs');
const path = require('path');
const { pathToFileURL } = require('url');
const engine = process.argv[2] || 'webkit';
const pw = require(engine === 'chromium' ? 'C:/Code/Kronikol4J/playwright/node_modules/playwright-core' : 'C:/Users/cex/.nuget/packages/microsoft.playwright/1.59.0/.playwright/package');
const svg = (w, extra, scroll) => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 120" width="${w}" style="display:block;border:1px solid #999;${extra || ''}">
<rect x="20" y="20" width="360" height="80" fill="#fdf6e4" stroke="#e0cf9f"/>
<foreignObject x="20" y="20" width="360" height="80"><div xmlns="http://www.w3.org/1999/xhtml" style="font:16px sans-serif;line-height:20px;height:100%;overflow:auto">${'The quick brown fox jumps over the lazy dog. '.repeat(scroll ? 12 : 1)}Line two of the box. Line three of the box. Line four.</div></foreignObject>
<text x="20" y="114" font-size="12" font-family="sans-serif">svg text at the same scale</text></svg>`;
const html = `<!doctype html><meta charset="utf-8"><body style="margin:10px;font:12px monospace">
<div>viewBox 400 wide, width=400 (scale 1)</div>${svg(400)}
<div>width=200 (scale 0.5)</div>${svg(200)}
<div>width=640 (scale 1.6)</div>${svg(640)}
<div>width=400, CSS transform: scale(0.5)</div>${svg(400, 'transform:scale(0.5);transform-origin:0 0')}
<div style="margin-top:-50px">scrollable box (content overflows): width=200 (scale 0.5)</div>${svg(200, '', true)}
<div>scrollable box: width=400 (scale 1)</div>${svg(400, '', true)}
</body>`;
(async () => {
  const f = path.join(__dirname, '_fo-minimal.html');
  fs.writeFileSync(f, html);
  const b = await pw[engine].launch();
  const page = await (await b.newContext({ viewport: { width: 700, height: 900 } })).newPage();
  await page.goto(pathToFileURL(f).href);
  await page.waitForTimeout(500);
  await page.screenshot({ path: path.join(__dirname, 'shots', `${engine}-d-foreignObject-minimal-scale.png`) });
  const r = await page.evaluate(() => [...document.querySelectorAll('foreignObject div')].map(d => { const r = d.getBoundingClientRect(); return [r.left, r.top, r.width, r.height].map(v => +v.toFixed(1)); }));
  console.log(engine, b.version(), JSON.stringify(r));
  await b.close();
})();
