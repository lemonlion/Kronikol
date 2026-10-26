// Builds the labelled overview sheets in overview/ from the per-engine screenshots in shots/.
const fs = require('fs');
const path = require('path');
const { pathToFileURL } = require('url');
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const S = f => path.join(__dirname, 'shots', f);
const E = ['chromium', 'firefox', 'webkit'];
const VER = { chromium: 'Chromium 149', firefox: 'Firefox 148', webkit: 'WebKit 26.4 (Windows)' };

const sheets = [
  {
    file: '1-highlight-api.png', zoom: 1.6, cols: 5,
    title: '(a) CSS Custom Highlight API: ::highlight(k) { background-color: magenta; color: green; fill: green } on a Range',
    items: E.flatMap(e => [['m-json', '"Fresh", SVG text, svg drawn at 0.71'], ['s1', 'SVG text, textLength + spacing'], ['m-cross', 'one Range over three <text>'], ['o-zwsp', '"chars):" + two U+200B'], ['o-box', 'HTML in the foreignObject box']]
      .map(([t, l]) => [`${VER[e]}: ${l}`, S(`${e}-a-highlight-${t}.png`)])),
  },
  {
    file: '2-rects-vs-text.png', zoom: 1.5, cols: 5,
    title: '(b, c) blue = Range.getClientRects(); red dashed = getExtentOfChar(); green dashed = getStartPositionOfChar + getSubStringLength; both mapped with getScreenCTM()',
    items: E.flatMap(e => [['Z0-m-json', 'svg at 0.71 (max-width)'], ['Z0-s1', 'textLength + spacing'], ['Z0-s2', 'textLength + spacingAndGlyphs'], ['Ztf-m-json', 'svg with transform: scale(1.5)'], ['Zanc-o-zwsp', 'ancestor transform: scale(.75), page scrolled']]
      .map(([t, l]) => [`${VER[e]}: ${l}`, S(`${e}-bc-rects-${t}.png`)])),
  },
  {
    file: '3-rect-vs-overlay-after-zoom.png', zoom: 1.4, cols: 4,
    title: '(e) magenta = <rect> inserted in the SVG (user space); cyan outline = absolutely positioned HTML overlay. Both placed at the starting zoom, then the zoom or scroll changed',
    items: E.flatMap(e => [['m-json-Zin', 'zoom in: svg CSS width x1.6'], ['m-json-Ztf', 'svg transform: scale(1.5)'], ['o-box-boxScroll', 'box scrolled 2 lines'], ['o-box-Zout', 'box, svg CSS width x0.5']]
      .map(([t, l]) => [`${VER[e]}: ${l}`, S(`${e}-e-overlay-${t}.png`)])),
  },
  {
    file: '4-many-matches-layer-vs-highlight.png', zoom: 1.3, cols: 2,
    title: 'All 28 matches of "Some_": one <g style="mix-blend-mode: multiply"> of yellow rects appended last (left) vs the highlight API (right)',
    items: E.flatMap(e => [[`${VER[e]}: multiply layer`, S(`${e}-g-multiply-layer-Some_.png`)], [`${VER[e]}: CSS.highlights`, S(`${e}-g-highlight-api-Some_.png`)]]),
  },
  {
    file: '5-foreignObject-box.png', zoom: 0.9, cols: 3,
    title: '(d) the long-note box: deep match brought into view, highlighted; and WebKit drawing a scrollable foreignObject box unscaled when the svg is scaled by its viewBox',
    items: [
      ...E.map(e => [`${VER[e]}: <mark>.scrollIntoView, svg width x0.5`, S(`${e}-d-box-scroll-mark-Zout.png`)]),
      ...E.map(e => [`${VER[e]}: range arithmetic, svg width x0.5`, S(`${e}-d-box-scroll-range-Zout.png`)]),
      ...E.map(e => [`${VER[e]}: live report, box at 50%`, S(`${e}-live-box-zoom50.png`)]),
    ],
  },
  {
    file: '6-foreignObject-minimal.png', zoom: 0.8, cols: 3,
    title: 'Minimal repro: the 5th picture is a scrollable foreignObject div in an svg drawn at half its viewBox width',
    items: E.map(e => [VER[e], S(`${e}-d-foreignObject-minimal-scale.png`)]),
  },
];

(async () => {
  fs.mkdirSync(path.join(__dirname, 'overview'), { recursive: true });
  const b = await pw.chromium.launch();
  const page = await (await b.newContext({ viewport: { width: 1600, height: 400 } })).newPage();
  for (const sh of sheets) {
    const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;');
    const cell = ([label0, file]) => { const label = esc(label0); return fs.existsSync(file)
      ? `<figure><figcaption>${label}</figcaption><img src="${pathToFileURL(file).href}" onload="this.style.width=(this.naturalWidth*${sh.zoom})+'px'"></figure>`
      : `<figure><figcaption>${label}</figcaption><div class="missing">(no picture)</div></figure>`; };
    const html = `<!doctype html><html><head><meta charset="utf-8"><style>
      body { margin: 10px; font: 13px system-ui, sans-serif; background: #fff; color: #222; }
      h1 { font-size: 14px; margin: 0 0 8px; max-width: 1500px; }
      .g { display: grid; grid-template-columns: repeat(${sh.cols}, max-content); gap: 10px 14px; align-items: start; }
      figure { margin: 0; } figcaption { font: 11px monospace; margin-bottom: 3px; color: #444; max-width: 420px; }
      img { display: block; border: 1px solid #bbb; }
      .missing { font: 11px monospace; color: #999; padding: 20px; border: 1px dashed #ccc; }
    </style></head><body><h1>${sh.title}</h1><div class="g">${sh.items.map(cell).join('')}</div></body></html>`;
    const tmp = path.join(__dirname, '_overview.html');
    fs.writeFileSync(tmp, html);
    await page.goto(pathToFileURL(tmp).href);
    await page.waitForTimeout(700);
    await page.screenshot({ path: path.join(__dirname, 'overview', sh.file), fullPage: true });
    console.log('overview/' + sh.file);
  }
  await b.close();
})();
