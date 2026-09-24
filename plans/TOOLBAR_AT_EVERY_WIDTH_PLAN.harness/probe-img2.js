const pw = require(require('path').resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
// The attachment image and its link under each cap, on the sweep's wide-content page (written to the E2E
// output folder by ViewportSweepTests), with a wide, a small and a tall image. Usage: node probe-img2.js
const path = require('path');
const fs = require('fs');
const out = path.resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/PlaywrightOutput');
fs.mkdirSync(path.join(out, 'attachments'), { recursive: true });
for (const [name, w, h] of [['screenshot.svg', 640, 120], ['small.svg', 100, 50], ['tall.svg', 300, 1000]])
  fs.writeFileSync(path.join(out, 'attachments', name), `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}"><rect width="${w}" height="${h}" fill="#8ab4f8"/></svg>`);
const variants = {
  'old 320px': '.attachment-image{max-width:320px !important}',
  'g: link min(322px,100%), img 100% border-box': '.attachment-image-link{max-width:min(322px,100%)}.attachment-image{max-width:100% !important;box-sizing:border-box}',
};
(async () => {
  const b = await pw.chromium.launch();
  for (const src of ['screenshot.svg', 'small.svg', 'tall.svg'])
  for (const w of [320, 1400]) for (const [name, css] of Object.entries(variants)) {
    const p = await b.newPage({ viewport: { width: w, height: 900 } });
    await p.goto('file:///' + path.join(out, 'SweepWideContent.html').split(path.sep).join('/'));
    if (css) await p.addStyleTag({ content: css });
    await p.evaluate(s => { document.querySelectorAll('details').forEach(d => d.open = true); document.querySelector('img.attachment-image').src = 'attachments/' + s; }, src);
    const img = p.locator('img.attachment-image').first();
    await img.scrollIntoViewIfNeeded();
    await p.waitForFunction(() => { const i = document.querySelector('img.attachment-image'); return i.complete && i.naturalWidth > 0; });
    const r = await img.evaluate(i => { const a = i.parentElement, s = i.closest('.step'); const e = s.getBoundingClientRect().left + s.clientLeft + s.clientWidth; const br = i.getBoundingClientRect();
      return `img ${br.width.toFixed(0)}x${br.height.toFixed(0)} (content ${i.clientWidth}x${i.clientHeight}, ratio ${(i.clientWidth / i.clientHeight).toFixed(3)} natural ${(i.naturalWidth / i.naturalHeight).toFixed(3)}) | link ${a.getBoundingClientRect().width.toFixed(0)} | past step ${(br.right - e).toFixed(0)}`; });
    console.log(`${src.padEnd(15)} ${w} ${name.padEnd(46)} ${r}`);
    await p.close();
  }
  await b.close();
})();
