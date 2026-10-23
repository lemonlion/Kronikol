// Why deep search reports "error" in Playwright's WebKit: probe what the search worker needs, inside a blob worker.
const { webkit, chromium } = require('playwright-core');
const { pathToFileURL } = require('url');
(async () => {
  for (const bt of [webkit, chromium]) {
    const browser = await bt.launch({ headless: true });
    const page = await browser.newPage();
    await page.goto(pathToFileURL(process.argv[2]).href, { waitUntil: 'commit' });
    await page.waitForFunction(() => window.__krnT && window.__krnT.paintDcl, null, { polling: 200, timeout: 120000 });
    const r = await page.evaluate(async () => {
      const code = `self.onmessage = async (e) => {
        const out = { ds: typeof DecompressionStream, resp: typeof Response, blob: typeof Blob, atob: typeof atob };
        try {
          const gz = new Uint8Array(e.data);
          const t = await new Response(new Blob([gz]).stream().pipeThrough(new DecompressionStream('gzip'))).text();
          out.gunzip = 'ok ' + t.length;
        } catch (err) { out.gunzip = 'ERR ' + err; }
        try {
          const b = await new Response(new Blob([new Uint8Array(e.data)]).stream().pipeThrough(new DecompressionStream('gzip'))).arrayBuffer();
          out.gunzipBytes = 'ok ' + b.byteLength;
        } catch (err) { out.gunzipBytes = 'ERR ' + err; }
        self.postMessage(out);
      };`;
      // a gzip of "hello" made by the page itself
      const gz = await new Response(new Blob(['hello hello hello']).stream().pipeThrough(new CompressionStream('gzip'))).arrayBuffer();
      const w = new Worker(URL.createObjectURL(new Blob([code], { type: 'application/javascript' })));
      const res = await new Promise((resolve) => { w.onmessage = e => resolve(e.data); w.onerror = e => resolve({ workerError: String(e.message) }); w.postMessage(gz); setTimeout(() => resolve({ timeout: true }), 10000); });
      // And the real search: what does the page's own worker say?
      const sb = document.getElementById('searchbar'); sb.value = 'participant'; sb.dispatchEvent(new KeyboardEvent('keyup', { bubbles: true }));
      await new Promise(r => setTimeout(r, 6000));
      return { probe: res, search: Object.assign({}, window.__kronikolSearch) };
    });
    console.log(bt.name(), JSON.stringify(r));
    await browser.close();
  }
})();
