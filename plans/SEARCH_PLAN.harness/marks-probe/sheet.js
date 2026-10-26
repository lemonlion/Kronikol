// Contact sheet: node sheet.js out.png zoom label1=file1 label2=file2 ... (files relative to this dir)
const fs = require('fs');
const path = require('path');
const { pathToFileURL } = require('url');
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const [outFile, zoomArg, ...pairs] = process.argv.slice(2);
const zoom = parseFloat(zoomArg) || 3;
(async () => {
  const items = pairs.map(p => { const i = p.indexOf('='); return { label: p.slice(0, i), file: path.resolve(__dirname, p.slice(i + 1)) }; }).filter(x => fs.existsSync(x.file));
  const html = `<!doctype html><html><head><meta charset="utf-8"><style>
    body { margin: 8px; font: 12px monospace; background: #fff; }
    .it { display: inline-block; vertical-align: top; margin: 4px 10px 10px 0; }
    .it div { margin-bottom: 2px; color: #333; }
    img { image-rendering: pixelated; border: 1px solid #ccc; display: block; }
  </style></head><body>${items.map(x => `<div class="it"><div>${x.label}</div><img src="${pathToFileURL(x.file).href}" onload="this.style.width=(this.naturalWidth*${zoom})+'px'"></div>`).join('')}</body></html>`;
  const tmp = path.join(__dirname, '_sheet.html');
  fs.writeFileSync(tmp, html);
  const b = await pw.chromium.launch();
  const page = await (await b.newContext({ viewport: { width: 1400, height: 400 } })).newPage();
  await page.goto(pathToFileURL(tmp).href);
  await page.waitForTimeout(500);
  await page.screenshot({ path: path.resolve(__dirname, outFile), fullPage: true });
  await b.close();
})();
