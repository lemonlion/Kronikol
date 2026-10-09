'use strict';
// The issue's own method (#162 check.js), on this machine: ONE Chrome started with --headless=new and a DevTools port,
// each page opened in a fresh tab over CDP, the verdict read from #comp-diagram every 500 ms. The browser runs on its
// own throwaway profile (never the user's). Pages are built by measure.js's shapes (same page, scripts and settings).
//   node cdp-check.js '<json>'  {label, jsflags, page:'w4'|'w0', cases:[{shape,L}|{n}], reps, exe}
const fs = require('fs'), path = require('path'), { spawn } = require('child_process'), zlib = require('zlib');
const ROOT = __dirname;
const cfg = JSON.parse(process.argv[2]);
const EXE = cfg.exe || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 9333;
const profile = path.join(ROOT, 'profiles', 'cdp-' + (cfg.jsflags ? 'off' : 'jit'));
fs.mkdirSync(profile, { recursive: true });
fs.mkdirSync(path.join(ROOT, 'logs'), { recursive: true });

// The page builder from measure.js, loaded without running its main().
const src = fs.readFileSync(path.join(ROOT, 'measure.js'), 'utf8').replace(/\nmain\(\)\.catch[\s\S]*$/, '\nmodule.exports = { writeCasePage, realPage };\n');
const m = new module.constructor(); m.paths = module.paths; m._compile(src.replace("const pw = require(PW);", 'const pw = null;'), path.join(ROOT, 'measure-lib.js'));
const { writeCasePage, realPage } = m.exports;

async function check(file) {
  const t = await (await fetch(`http://127.0.0.1:${PORT}/json/new?about:blank`, { method: 'PUT' })).json();
  const ws = new WebSocket(t.webSocketDebuggerUrl);
  let id = 0; const pending = new Map();
  ws.onmessage = (msg) => { const o = JSON.parse(msg.data); if (o.id && pending.has(o.id)) { pending.get(o.id)(o); pending.delete(o.id); } };
  await new Promise(r => ws.onopen = r);
  const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
  await send('Page.enable');
  await send('Page.navigate', { url: 'file:///' + file.replace(/\\/g, '/') });
  let verdict = 'TIMEOUT', detail = '';
  for (const start = Date.now(); Date.now() - start < 60000;) {
    await new Promise(r => setTimeout(r, 500));
    const ev = await send('Runtime.evaluate', { returnByValue: true, expression: `(() => {
      const svg = document.querySelector('#comp-diagram svg'); if (!svg) return 'PENDING';
      const t = svg.textContent;
      if (/RangeError|Maximum call stack/.test(t)) return 'STACK';
      if (/Syntax Error|An error has occurred/.test(t)) return 'ERROR';
      return t.indexOf('Caller') >= 0 && t.indexOf('Warehouse') >= 0 ? 'DRAWS' : 'NONAMES'; })()` });
    const v = ev.result && ev.result.result && ev.result.result.value;
    if (v !== 'PENDING') { verdict = v; break; }
  }
  const tel = await send('Runtime.evaluate', { returnByValue: true, expression: `JSON.stringify({ mode: window.__kronikolRender.mode, workers: window.__kronikolRender.workers, ua: navigator.userAgent })` });
  detail = tel.result.result.value;
  ws.close(); await fetch(`http://127.0.0.1:${PORT}/json/close/${t.id}`);
  return { verdict, detail };
}

(async () => {
  const args = ['--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${profile}`, '--no-first-run', '--no-default-browser-check', 'about:blank'];
  if (cfg.jsflags) args.unshift('--js-flags=' + cfg.jsflags);
  const chrome = spawn(EXE, args, { stdio: 'ignore' });
  try {
    let version = null;
    for (let i = 0; i < 100 && !version; i++) {
      try { version = await (await fetch(`http://127.0.0.1:${PORT}/json/version`)).json(); } catch { await new Promise(r => setTimeout(r, 200)); }
    }
    console.log(`# ${version.Browser} ${cfg.jsflags || 'JIT on'} page ${cfg.page}, one browser, a fresh tab per page`);
    const out = [];
    for (let rep = 0; rep < (cfg.reps || 1); rep++) {
      for (const c of cfg.cases) {
        const file = c.n !== undefined ? realPage(cfg.page, c.n) : writeCasePage(cfg.page, c.shape, c.L);
        const r = await check(file);
        const row = Object.assign({ t: new Date().toISOString(), label: cfg.label, browser: version.Browser, jsflags: cfg.jsflags || '', page: cfg.page, rep }, c, r);
        fs.appendFileSync(path.join(ROOT, 'logs', cfg.label + '.jsonl'), JSON.stringify(row) + '\n');
        console.log(`${c.shape || 'N=' + c.n} ${c.L || ''} rep ${rep}: ${r.verdict} ${r.detail}`);
        out.push(row);
      }
    }
  } finally {
    chrome.kill();
  }
})().catch(e => { console.error(e); process.exit(1); });
