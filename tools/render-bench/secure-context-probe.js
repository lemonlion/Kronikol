'use strict';
// Where is crypto.subtle available (page and Blob worker) on file://, loopback http and LAN http; and what does hashing the engine cost (subtle vs a plain-JS SHA-256)?
const http = require('http'), os = require('os'), fs = require('fs'), path = require('path'), url = require('url');
const pw = require('C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package');
const html = '<!doctype html><html><body>probe</body></html>';
const engine = fs.readFileSync('C:/Code/Kronikol/tools/render-bench/core-npm-1.2026.8.js');
const server = http.createServer((req, res) => {
  res.setHeader('access-control-allow-origin', '*');
  if (req.url === '/engine.js') { res.setHeader('content-type', 'application/javascript'); return res.end(engine); }
  res.setHeader('content-type', 'text/html'); res.end(html);
});
const lan = Object.values(os.networkInterfaces()).flat().find(i => i && i.family === 'IPv4' && !i.internal);
const filePage = path.join(__dirname, 'probe.html'); fs.writeFileSync(filePage, html);
const fileUrl = url.pathToFileURL(filePage).href;
const probeFn = async ({ engineUrl }) => {
  function sha256(bytes) {
    const K = new Uint32Array([0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2]);
    const H = new Uint32Array([0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19]);
    const len = bytes.length, padLen = Math.ceil((len + 9) / 64) * 64;
    const p = new Uint8Array(padLen); p.set(bytes); p[len] = 0x80;
    const dv = new DataView(p.buffer); const bitLen = len * 8;
    dv.setUint32(padLen - 8, Math.floor(bitLen / 4294967296)); dv.setUint32(padLen - 4, bitLen % 4294967296);
    const W = new Uint32Array(64);
    for (let off = 0; off < padLen; off += 64) {
      for (let i = 0; i < 16; i++) W[i] = dv.getUint32(off + i * 4);
      for (let i = 16; i < 64; i++) { const x = W[i-15], y = W[i-2]; const s0 = ((x >>> 7) | (x << 25)) ^ ((x >>> 18) | (x << 14)) ^ (x >>> 3); const s1 = ((y >>> 17) | (y << 15)) ^ ((y >>> 19) | (y << 13)) ^ (y >>> 10); W[i] = (W[i-16] + s0 + W[i-7] + s1) | 0; }
      let a = H[0], b = H[1], c = H[2], d = H[3], e = H[4], f = H[5], g = H[6], h = H[7];
      for (let i = 0; i < 64; i++) {
        const S1 = ((e >>> 6) | (e << 26)) ^ ((e >>> 11) | (e << 21)) ^ ((e >>> 25) | (e << 7));
        const ch = (e & f) ^ (~e & g);
        const t1 = (h + S1 + ch + K[i] + W[i]) | 0;
        const S0 = ((a >>> 2) | (a << 30)) ^ ((a >>> 13) | (a << 19)) ^ ((a >>> 22) | (a << 10));
        const maj = (a & b) ^ (a & c) ^ (b & c);
        const t2 = (S0 + maj) | 0;
        h = g; g = f; f = e; e = (d + t1) | 0; d = c; c = b; b = a; a = (t1 + t2) | 0;
      }
      H[0] = (H[0] + a) | 0; H[1] = (H[1] + b) | 0; H[2] = (H[2] + c) | 0; H[3] = (H[3] + d) | 0; H[4] = (H[4] + e) | 0; H[5] = (H[5] + f) | 0; H[6] = (H[6] + g) | 0; H[7] = (H[7] + h) | 0;
    }
    return Array.from(H).map(v => (v >>> 0).toString(16).padStart(8, '0')).join('');
  }
  const page = { secure: window.isSecureContext, subtle: typeof (self.crypto && self.crypto.subtle), origin: location.origin };
  const worker = await new Promise((res) => {
    try {
      const src = 'self.postMessage({ secure: self.isSecureContext, subtle: typeof (self.crypto && self.crypto.subtle) });';
      const w = new Worker(URL.createObjectURL(new Blob([src], { type: 'application/javascript' })));
      w.onmessage = (e) => res(e.data); w.onerror = (e) => res({ error: String(e.message || e) });
      setTimeout(() => res({ error: 'timeout' }), 3000);
    } catch (e) { res({ error: String(e.message || e) }); }
  });
  const jsAbc = sha256(new TextEncoder().encode('abc')).slice(0, 8);
  let r; try { r = await fetch(engineUrl); } catch (e) { return { page, worker, jsAbc, fetchError: String(e.message || e) }; }
  const buf = await r.arrayBuffer(); const bytes = new Uint8Array(buf);
  let subtleMs = null, subtleHex = null;
  if (self.crypto && self.crypto.subtle) { const t0 = performance.now(); const d = await crypto.subtle.digest('SHA-256', buf); subtleMs = Math.round(performance.now() - t0); subtleHex = Array.from(new Uint8Array(d)).map(b => b.toString(16).padStart(2, '0')).join('').slice(0, 12); }
  const t1 = performance.now(); const jsHex = sha256(bytes).slice(0, 12); const jsMs = Math.round(performance.now() - t1);
  return { page, worker, jsAbc, bytes: bytes.length, subtleMs, subtleHex, jsMs, jsHex };
};
(async () => {
  await new Promise(r => server.listen(0, '0.0.0.0', r));
  const port = server.address().port;
  const targets = [
    ['file://', fileUrl, 'http://127.0.0.1:' + port + '/engine.js'],
    ['http loopback', 'http://127.0.0.1:' + port + '/', 'http://127.0.0.1:' + port + '/engine.js'],
    ['http LAN ' + (lan ? lan.address : 'n/a'), lan ? 'http://' + lan.address + ':' + port + '/' : null, lan ? 'http://' + lan.address + ':' + port + '/engine.js' : null],
  ];
  for (const name of ['chromium', 'firefox', 'webkit']) {
    let browser;
    try { browser = await pw[name].launch({ headless: true }); } catch (e) { console.log(name + ': not available (' + String(e.message).slice(0, 60) + ')'); continue; }
    for (const [label, pageUrl, engineUrl] of targets) {
      if (!pageUrl) continue;
      const page = await browser.newPage();
      try { await page.goto(pageUrl); const r = await page.evaluate(probeFn, { engineUrl }); console.log(name + ' | ' + label + ' | ' + JSON.stringify(r)); }
      catch (e) { console.log(name + ' | ' + label + ' | ERROR ' + String(e.message).slice(0, 160)); }
      await page.close();
    }
    await browser.close();
  }
  server.close();
})();
