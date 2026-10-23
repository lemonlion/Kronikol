// Step S0: whole-browser private bytes (every process the launched browser started, as Windows counts them),
// less the same browser on about:blank, for today's page and the C2 layouts. Three points: the report up and the
// diagram sources decoded (one frame) or not (pieces); after drawing the diagrams of the first scenarios that have
// any; after a deep search that verifies every scenario ("participant" is in every sequence diagram's source).
// node mem.js <browser> <file1.html,...>
const { chromium, firefox, webkit } = require('playwright-core');
const { pathToFileURL } = require('url');
const { execSync } = require('child_process');

const bname = process.argv[2];
const files = process.argv[3].split(',');
const MB = b => Math.round(b / 1048576);

function treePrivate(rootPid) {
  const out = execSync('powershell -NoProfile -Command "Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId,PrivatePageCount | ConvertTo-Json -Compress"', { maxBuffer: 64 << 20 }).toString();
  const procs = JSON.parse(out);
  const kids = new Map();
  for (const p of procs) { if (!kids.has(p.ParentProcessId)) kids.set(p.ParentProcessId, []); kids.get(p.ParentProcessId).push(p); }
  let total = 0, n = 0;
  const walk = pid => { for (const c of kids.get(pid) || []) { total += Number(c.PrivatePageCount); n++; walk(c.ProcessId); } };
  const me = procs.find(p => p.ProcessId === rootPid);
  if (me) { total += Number(me.PrivatePageCount); n++; }
  walk(rootPid);
  return { total, n };
}

async function settle(page, ms) {
  if (bname === 'chromium') {
    const cdp = await page.context().newCDPSession(page);
    await cdp.send('HeapProfiler.collectGarbage').catch(() => {});
  }
  await page.waitForTimeout(ms);
}

(async () => {
  const bt = { chromium, firefox, webkit }[bname];
  for (const file of files) {
    const server = await bt.launchServer({ headless: true });
    const pid = server.process().pid;
    const browser = await bt.connect(server.wsEndpoint());
    const context = await browser.newContext({ viewport: { width: 1400, height: 900 } });
    const page = await context.newPage();
    await page.goto('about:blank');
    await page.waitForTimeout(3000);
    const base = treePrivate(pid);
    await page.goto(pathToFileURL(file).href, { waitUntil: 'commit' });
    await page.waitForFunction(() => { const T = window.__krnT; return !!(T && T.paintDcl && (!T.diagStart || T.diagDone)); }, null, { polling: 200, timeout: 180000 });
    await settle(page, 4000);
    const up = treePrivate(pid);
    // Draw the diagrams of the first six scenarios that have any
    const drawn = await page.evaluate(async () => {
      const els = [...document.querySelectorAll('.scenario')].filter(s => s.querySelector('.plantuml-browser[id]')).slice(0, 6);
      for (const s of els) { let d = s; while (d) { if (d.tagName === 'DETAILS') d.open = true; d = d.parentElement; } s.querySelectorAll('details').forEach(x => x.open = true); }
      let n = 0;
      for (const s of els) { s.scrollIntoView(); await new Promise(r => setTimeout(r, 1500)); }
      const t0 = Date.now();
      while (Date.now() - t0 < 60000) {
        n = els.reduce((a, s) => a + s.querySelectorAll('.plantuml-browser[id] svg').length, 0);
        if (n >= els.length) break;
        await new Promise(r => setTimeout(r, 500));
      }
      return n;
    });
    await settle(page, 3000);
    const afterDraw = treePrivate(pid);
    // A deep search that verifies every scenario
    await page.evaluate(() => { const sb = document.getElementById('searchbar'); sb.value = 'participant'; sb.dispatchEvent(new KeyboardEvent('keyup', { bubbles: true })); });
    let last = '', since = Date.now(); const t0 = Date.now();
    while (Date.now() - t0 < 120000) {
      await page.waitForTimeout(500);
      const now = await page.evaluate(() => (window.__kronikolSearch || {}).indexState + ':' + [...document.getElementsByClassName('scenario')].filter(s => s.offsetParent !== null).length);
      if (now !== last) { last = now; since = Date.now(); }
      if (Date.now() - since > 4000) break;
    }
    const afterSearch = treePrivate(pid);
    await settle(page, 3000);
    const afterSearchSettled = treePrivate(pid);
    const name = file.split(/[\\/]/).pop();
    console.log(`${bname}\t${name}\tbase ${MB(base.total)} MB (${base.n} procs)\tup +${MB(up.total - base.total)}\tdrawn(${drawn}) +${MB(afterDraw.total - base.total)}\tsearch(${last}) +${MB(afterSearch.total - base.total)}, settled +${MB(afterSearchSettled.total - base.total)}`);
    await browser.close();
    await server.close();
  }
})().catch(e => { console.error(e); process.exit(1); });
