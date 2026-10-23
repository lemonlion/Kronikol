// fzstd 0.1.1 against zstddec 0.3.4 (libzstd in Wasm) inside each engine, on the main thread of a blank file:// page:
// the page frame and the diagram frame of a prototype, interleaved, five runs (the first is a warm-up and is dropped).
// node dec-browser.js <dir holding krn-z1.zst and krn-d0.zst> [browsers csv]
const fs = require('fs'), path = require('path'), os = require('os');
const { chromium, firefox, webkit } = require('playwright-core');
const { pathToFileURL } = require('url');

const dir = process.argv[2];
const browsers = (process.argv[3] || 'chromium,firefox,webkit').split(',');
const fz = fs.readFileSync(path.join(__dirname, 'node_modules/fzstd/umd/index.js'), 'utf8');
const zd = fs.readFileSync(path.join(__dirname, 'node_modules/zstddec/dist/zstddec.mjs'), 'utf8')
  .replace(/export\s*\{\s*ZSTDDecoder\s*\};?\s*$/, 'window.ZSTDDecoder = ZSTDDecoder;');
const frames = ['krn-z1.zst', 'krn-d0.zst'].map(f => fs.readFileSync(path.join(dir, f)).toString('base64'));
const blank = path.join(os.tmpdir(), 'dec-browser-blank.html');
fs.writeFileSync(blank, '<!doctype html><meta charset="utf-8"><title>dec</title><body></body>');

(async () => {
  for (const b of browsers) {
    const browser = await ({ chromium, firefox, webkit })[b].launch({ headless: true });
    const page = await browser.newPage();
    await page.goto(pathToFileURL(blank).href);
    await page.addScriptTag({ content: fz });
    await page.addScriptTag({ content: zd, type: 'module' });
    await page.waitForFunction(() => !!window.ZSTDDecoder, null, { polling: 100, timeout: 20000 });
    const r = await page.evaluate(async (b64s) => {
      const bytes = b64s.map(s => Uint8Array.fromBase64 ? Uint8Array.fromBase64(s) : Uint8Array.from(atob(s), c => c.charCodeAt(0)));
      const dec = new ZSTDDecoder(); await dec.init();
      const out = [];
      for (const z of bytes) {
        const ref = fzstd.decompress(z);
        const t = { fz: [], zd: [] };
        let same = true;
        for (let i = 0; i < 5; i++) {
          let s = performance.now(); const a = fzstd.decompress(z); const ta = performance.now() - s;
          s = performance.now(); const c = dec.decode(z, ref.length); const tc = performance.now() - s;
          if (i > 0) { t.fz.push(Math.round(ta)); t.zd.push(Math.round(tc)); }
          if (c.length !== a.length) same = false;
          else for (let k = 0; k < c.length; k += 4099) if (c[k] !== a[k]) { same = false; break; }
        }
        out.push({ in: z.length, out: ref.length, same, fz: t.fz.join('/'), zd: t.zd.join('/') });
      }
      return out;
    }, frames);
    for (const [i, x] of r.entries())
      console.log(`${b}\t${i ? 'diagrams' : 'page'}\tin ${x.in} out ${x.out}\tsame=${x.same}\tfzstd ms ${x.fz}\tzstddec ms ${x.zd}`);
    await browser.close();
  }
})().catch(e => { console.error(e); process.exit(1); });
