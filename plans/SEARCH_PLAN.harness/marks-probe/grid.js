// Labelled grid of screenshots: node grid.js out.png "Title" cols zoom label=file ... (files relative to this dir)
const fs = require('fs');
const path = require('path');
const { pathToFileURL } = require('url');
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const [outFile, title, colsArg, zoomArg, ...pairs] = process.argv.slice(2);
const cols = parseInt(colsArg, 10) || 3, zoom = parseFloat(zoomArg) || 1;
(async () => {
  const items = pairs.map(p => { const i = p.indexOf('='); return { label: p.slice(0, i), file: path.resolve(__dirname, p.slice(i + 1)) }; });
  const cell = x => fs.existsSync(x.file)
    ? `<figure><figcaption>${x.label}</figcaption><img src="${pathToFileURL(x.file).href}" onload="this.style.width=(this.naturalWidth*${zoom})+'px'"></figure>`
    : `<figure><figcaption>${x.label}</figcaption><div class="missing">(no picture)</div></figure>`;
  const html = `<!doctype html><html><head><meta charset="utf-8"><style>
    body { margin: 10px; font: 13px system-ui, sans-serif; background: #fff; color: #222; }
    h1 { font-size: 15px; margin: 0 0 8px; }
    .g { display: grid; grid-template-columns: repeat(${cols}, max-content); gap: 10px 14px; align-items: start; }
    figure { margin: 0; } figcaption { font: 11px monospace; margin-bottom: 3px; color: #444; }
    img { display: block; border: 1px solid #bbb; image-rendering: auto; }
    .missing { font: 11px monospace; color: #999; padding: 20px; border: 1px dashed #ccc; }
  </style></head><body><h1>${title}</h1><div class="g">${items.map(cell).join('')}</div></body></html>`;
  const tmp = path.join(__dirname, '_grid.html');
  fs.writeFileSync(tmp, html);
  const b = await pw.chromium.launch();
  const page = await (await b.newContext({ viewport: { width: 1600, height: 400 } })).newPage();
  await page.goto(pathToFileURL(tmp).href);
  await page.waitForTimeout(600);
  await page.screenshot({ path: path.resolve(__dirname, outFile), fullPage: true });
  await b.close();
})();
