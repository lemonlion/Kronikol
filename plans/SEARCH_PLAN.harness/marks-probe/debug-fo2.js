// WebKit: is the foreignObject box drawn unscaled after a zoom because of a missed repaint, or always?
const fs = require('fs');
const path = require('path');
const { pathToFileURL } = require('url');
const engine = process.argv[2] || 'webkit';
const pw = require(engine === 'chromium' ? 'C:/Code/Kronikol4J/playwright/node_modules/playwright-core' : 'C:/Users/cex/.nuget/packages/microsoft.playwright/1.59.0/.playwright/package');
(async () => {
  const b = await pw[engine].launch();
  const ctx = await b.newContext({ viewport: { width: 1400, height: 1000 } });
  // 1. a page whose box svg is drawn at half size from the start (inline style in the markup)
  const html = fs.readFileSync(path.join(__dirname, 'markup.html'), 'utf8');
  const i = html.indexOf('<div id="d2" class="plantuml-browser"><svg ');
  const half = html.slice(0, i) + html.slice(i).replace('<svg ', '<svg style="max-width:100%;width:561.5px" ');
  fs.writeFileSync(path.join(__dirname, '_markup-half.html'), half);
  const shots = [];
  const page = await ctx.newPage();
  for (const [label, url, act] of [
    ['static-half', '_markup-half.html', null],
    ['dynamic-half', 'markup.html', 'half'],
    ['dynamic-half+relayout', 'markup.html', 'half-relayout'],
    ['dynamic-half+rewrite-fo-width', 'markup.html', 'half-fo'],
  ]) {
    await page.goto(pathToFileURL(path.join(__dirname, url)).href);
    await page.waitForTimeout(800);
    const info = await page.evaluate(async (act) => {
      const svg = document.querySelector('#d2 svg'), fo = svg.querySelector('foreignObject'), div = fo.querySelector('div');
      const raf2 = () => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
      if (act) { svg.style.maxWidth = '100%'; svg.style.width = '561.5px'; await raf2(); }
      if (act === 'half-relayout') { svg.style.display = 'none'; svg.getBoundingClientRect(); svg.style.display = ''; await raf2(); }
      if (act === 'half-fo') { const w = fo.getAttribute('width'); fo.setAttribute('width', +w + 1); await raf2(); fo.setAttribute('width', w); await raf2(); }
      fo.scrollIntoView({ block: 'start' }); window.scrollBy(0, -40); await raf2();
      const d = div.getBoundingClientRect();
      return { div: [d.left, d.top, d.width, d.height].map(v => +v.toFixed(1)) };
    }, act);
    const clip = { x: Math.max(0, Math.floor(info.div[0] - 20)), y: Math.max(0, Math.floor(info.div[1] - 20)), width: 480, height: 420 };
    const f = path.join(__dirname, 'shots', `${engine}-d-box-at-half-${label}.png`);
    await page.screenshot({ path: f, clip });
    console.log(engine, label, JSON.stringify(info));
  }
  await b.close();
})();
