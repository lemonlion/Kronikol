// statement-limits-worker-probe.js: PlantUmlStatementLimits measured where BrowserJs renders, in the shipped
// worker, instead of in node (statement-limits-probe.js). The page is the real render script with one worker
// (emitter-corpus -- --shim <page> 1); the engine comes from each given CDN base.
//   node statement-limits-worker-probe.js <shim page> <label>=<cdn base> [...]
// Message sources follow plans/DIAGRAM_COLOURS_PLAN.harness/statement-node.js (its F25, R30): the emitter's
// capped request statement for a 2,300-character query, cut to each length, with and without its
// [[#iflow-…]] link. Block labels and coloured bars carry the same query text.
//
//   node statement-limits-worker-probe.js --scan <linked dir> <shim page> <label>=<cdn base> [...]
// plans/ENGINE_PIN_PLAN.md S0: the text inside the internal-flow link, per label shape, cut the way
// PlantUmlStatementLimits.TruncateLabel cuts it, at every length from FROM to TO in steps of STEP (default 300 to
// 1975 by 25; the edge is not monotonic, so a scan, not a bisection). The sources come from
// emitter-corpus -- --linked-labels <dir>. BROWSER=chromium|firefox|webkit (default chromium); a shim page written
// with 0 workers measures the main-thread path instead of the worker.
const fs = require('fs'), path = require('path'), url = require('url'), os = require('os');
const pw = require(path.resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
if (process.argv[2] === '--scan') scan(); else legacy();

function legacy() {
const [shim, ...bases] = process.argv.slice(2);
const base = fs.readFileSync(path.resolve(__dirname, '../../plans/DIAGRAM_COLOURS_PLAN.harness/payload-longpath.puml'), 'utf8').replace(/\r\n/g, '\n');
const st = base.split('\n').find(l => l.includes('[[#iflow-'));
const m = /^(.*?: )\[\[#iflow-[0-9a-f-]+ (.*)\]\]$/.exec(st);
const label = m[2].replace(/…$/, '') + Array.from({ length: 60 }, (_, i) => '&extra' + i + '=value' + i).join('');
const cut = (s, n) => s.slice(0, n - 1) + '…';
const head = st.slice(0, st.indexOf(m[2]));
const cases = [];
for (const n of [1000, 1100, 1200, 1300, 1400, 1500, 1600, 1700, 1800, 1900, 2000])
  cases.push({ kind: 'message+link', n, src: base.replace(st, head + cut(label, n - head.length - 2) + ']]') });
for (const n of [1000, 1500, 1800, 2000]) cases.push({ kind: 'message', n, src: base.replace(st, m[1] + cut(label, n - m[1].length)) });
const mini = body => `@startuml\n!pragma teoz true\nparticipant A\nparticipant B\nA -> B : one\n${body}\nB --> A : two\n@enduml`;
for (const n of [800, 1000, 1200, 1300, 1400, 1471, 1600, 2000]) cases.push({ kind: 'loop label', n, src: mini(`loop ${cut(label, n)}\nA -> B : in\nend`) });
const bar = 'hnote across #black:<color:white>';
for (const n of [800, 1000, 1200, 1300, 1400, 1600, 2000]) cases.push({ kind: 'coloured bar', n, src: mini(bar + cut(label, n - bar.length)) });
const shimHtml = fs.readFileSync(shim, 'utf8');
// The CDN base the shim page was written with: TrackingDefaults.PlantUmlJsCdnBase of the build that wrote it.
const pinned = process.env.PINNED || /PlantUmlJsCdnBase = "([^"]+)"/.exec(fs.readFileSync(path.resolve(__dirname, '../../src/Kronikol/Constants/TrackingDefaults.cs'), 'utf8'))[1];
if (!shimHtml.includes(pinned)) throw new Error('the shim page does not carry ' + pinned + '; set PINNED to the base it was written with');
(async () => {
  const browser = await pw.chromium.launch({ args: process.env.JSFLAGS ? ['--js-flags=' + process.env.JSFLAGS] : [] });
  const out = {};
  for (const arg of bases) {
    const i = arg.indexOf('='), lbl = arg.slice(0, i), cdn = arg.slice(i + 1);
    const pagePath = path.join(os.tmpdir(), `statement-worker-${lbl}.html`);
    fs.writeFileSync(pagePath, shimHtml.split(pinned).join(cdn));
    const page = await browser.newPage();
    await page.goto(url.pathToFileURL(pagePath).href);
    await page.waitForFunction(() => window.__kronikolRender && window.__kronikolRender.mode === 'worker' && window.__kronikolRender.workers > 0, null, { timeout: 120000, polling: 200 });
    for (const [k, c] of cases.entries()) {
      const r = await page.evaluate(([src, id]) => new Promise(resolve => {
        const el = document.createElement('div'); el.id = id; document.body.appendChild(el);
        const done = v => { clearInterval(t); clearTimeout(to); resolve(v); };
        const t = setInterval(() => { if (el.querySelector('svg')) done('drawn'); else if (el.textContent.trim()) done(el.textContent.trim().slice(0, 90)); }, 50);
        const to = setTimeout(() => done('nothing in 60 s'), 60000);
        window.plantuml.render(src.split('\n'), id);
      }), [c.src, `c${k}-${lbl}`]);
      (out[k] = out[k] || { kind: c.kind, n: c.n })[lbl] = r;
    }
    await page.close();
  }
  await browser.close();
  console.log('base', bases.join(' '), '| Chromium, 1 worker, file://');
  for (const row of Object.values(out)) console.log(JSON.stringify(row));
})().catch(e => { console.error(e); process.exit(1); });
}

// PlantUmlStatementLimits.TruncateLabel, line for line: never inside a `<U+hhhh>` escape, never stranding a backslash.
function truncateLabel(label, budget) {
  const marker = '…';
  if (budget <= 0) return '';
  if (label.length <= budget) return label;
  let cut = Math.max(0, budget - marker.length);
  for (let p = cut - 1; p >= 0 && p >= cut - 10; p--) {
    if (label[p] === '>') break;
    if (label[p] !== '<') continue;
    if (label.startsWith('<U+', p)) cut = p;
    break;
  }
  let slashes = 0;
  while (cut - slashes > 0 && label[cut - slashes - 1] === '\\') slashes++;
  if (slashes % 2 === 1) cut--;
  return label.slice(0, cut) + marker;
}

function scan() {
  const [, , , dir, shim, ...bases] = process.argv;
  const from = +(process.env.FROM || 300), to = +(process.env.TO || 1975), step = +(process.env.STEP || 25);
  const browserName = process.env.BROWSER || 'chromium';
  const shapes = fs.readdirSync(dir).filter(f => /^linked-.*\.puml$/.test(f)).sort();
  const cases = [];
  for (const file of shapes) {
    const source = fs.readFileSync(path.join(dir, file), 'utf8').replace(/\r\n/g, '\n');
    const statement = source.split('\n').find(l => l.includes('[[#iflow-'));
    // The request arrow, `a -> b: [[#iflow-<id> <label>]]`, or a component edge, `a --> b : "[[#iflow-rel-… <methods>]]\n…"`.
    const m = /^(.*?)\[\[#(iflow-[^\s\]]+) (.*?)\]\](.*)$/.exec(statement);
    if (!m) throw new Error(file + ': no linked statement');
    const full = m[3].replace(/…$/, '');
    const sequence = source.includes('!pragma teoz');
    // ASIS=1: the source exactly as the emitter wrote it, one case per shape (the check after a fix).
    if (process.env.ASIS === '1') {
      cases.push({ shape: file.slice(7, -5), n: m[3].length, chars: m[3].length, statement: statement.trim().length, sequence, src: source });
      continue;
    }
    for (let n = from; n <= to; n += step) {
      if (n > full.length) break;
      const text = truncateLabel(full, n);
      // UNLINKED=1: the same label without its link, as the emitter writes it with internal-flow tracking off.
      const cutStatement = process.env.UNLINKED === '1' ? `${m[1]}${text}${m[4]}` : `${m[1]}[[#${m[2]} ${text}]]${m[4]}`;
      cases.push({ shape: file.slice(7, -5), n, chars: text.length, statement: cutStatement.trim().length, sequence,
        src: source.replace(statement, cutStatement) });
    }
  }
  const shimHtml = fs.readFileSync(shim, 'utf8');
  const pinned = process.env.PINNED || /PlantUmlJsCdnBase = "([^"]+)"/.exec(fs.readFileSync(path.resolve(__dirname, '../../src/Kronikol/Constants/TrackingDefaults.cs'), 'utf8'))[1];
  if (!shimHtml.includes(pinned)) throw new Error('the shim page does not carry ' + pinned + '; set PINNED to the base it was written with');
  // COLD=1: every case gets a fresh page, so its render is the first one in a new worker (the edge moves with V8's
  // tier-up: interpreted frames are larger, and a warmed-up worker draws labels a cold one loses). CONCURRENCY=<n>
  // pages at once in that mode. JSFLAGS is handed to Chromium as --js-flags (for instance "--no-opt --no-maglev",
  // interpreter and baseline frames only: the lower bound of the edge).
  const cold = process.env.COLD === '1';
  const concurrency = +(process.env.CONCURRENCY || 1);
  const launchArgs = process.env.JSFLAGS ? ['--js-flags=' + process.env.JSFLAGS] : [];
  const openPage = async (browser, pagePath) => {
    const page = await browser.newPage();
    await page.goto(url.pathToFileURL(pagePath).href);
    await page.waitForFunction(() => window.__kronikolRender && window.__kronikolRender.engineReadyAt !== null
      || (document.body && document.body.textContent.indexOf('Render error') >= 0), null, { timeout: 180000, polling: 200 });
    return page;
  };
  (async () => {
    // LAUNCH: JSON merged into the launch options, e.g. '{"firefoxUserPrefs":{"javascript.options.ion":false}}' or
    // '{"env":{"JSC_useJIT":"false"}}' for WebKit (JavaScriptCore reads JSC_ options from the environment).
    const extra = process.env.LAUNCH ? JSON.parse(process.env.LAUNCH) : {};
    const browser = await pw[browserName].launch(Object.assign(browserName === 'chromium' ? { args: launchArgs } : {}, extra));
    const rows = [];
    for (const arg of bases) {
      const i = arg.indexOf('='), lbl = arg.slice(0, i), cdn = arg.slice(i + 1);
      const pagePath = path.join(os.tmpdir(), `statement-scan-${lbl}-${browserName}.html`);
      fs.writeFileSync(pagePath, shimHtml.split(pinned).join(cdn));
      const page = await openPage(browser, pagePath);
      const mode = await page.evaluate(() => window.__kronikolRender.mode + (window.__kronikolRender.fallbackReason ? ' (' + window.__kronikolRender.fallbackReason + ')' : ''));
      const renderOn = (pg, c, k) => pg.evaluate(([src, id, sequence]) => new Promise(resolve => {
          const el = document.createElement('div'); el.id = id; document.body.appendChild(el);
          const done = v => { clearInterval(t); clearTimeout(to); el.remove(); resolve(v); };
          const t = setInterval(() => {
            const svg = el.querySelector('svg');
            if (svg) {
              const text = svg.textContent;
              // A sequence diagram draws each participant twice (head and foot box); the engine's error picture
              // and the class-diagram fallback do not.
              const heads = (text.match(/OrderService/g) || []).length;
              // The engine's error pictures: a syntax error, a stack overflow, and "An error has occurred!" (for
              // instance Graphviz without WebAssembly, which --jitless turns off).
              done(/RangeError|Maximum call stack/.test(text) ? 'STACK-PICTURE' : /Syntax Error|An error has occurred/.test(text) ? 'ERROR-PICTURE'
                : !sequence || heads >= 2 ? 'drawn' : 'drawn-once');
            } else if (el.textContent.trim()) {
              const msg = el.textContent.trim();
              done(/Maximum call stack size exceeded|too much recursion/i.test(msg) ? 'STACK' : msg.slice(0, 90));
            }
          }, 25);
          const to = setTimeout(() => done('nothing in 60 s'), 60000);
          window.plantuml.render(src.split('\n'), id);
        }), [c.src, `s${k}-${lbl}`, c.sequence]);
      if (!cold) {
        for (const [k, c] of cases.entries())
          rows.push({ build: lbl, shape: c.shape, n: c.n, chars: c.chars, statement: c.statement, result: await renderOn(page, c, k) });
      } else {
        let next = 0;
        const lane = async () => {
          while (next < cases.length) {
            const k = next++, c = cases[k];
            const pg = await openPage(browser, pagePath);
            rows.push({ build: lbl, shape: c.shape, n: c.n, chars: c.chars, statement: c.statement, result: await renderOn(pg, c, k) });
            await pg.close();
          }
        };
        await Promise.all(Array.from({ length: concurrency }, lane));
      }
      console.log(`# ${lbl} ${cdn} | ${browserName} ${browser.version()}, ${mode}${cold ? ', a fresh page per case' : ', one page'}${launchArgs.length ? ', ' + launchArgs.join(' ') : ''}, file://`);
      await page.close();
    }
    rows.sort((a, b) => a.build.localeCompare(b.build) || a.shape.localeCompare(b.shape) || a.n - b.n);
    await browser.close();
    for (const row of rows) console.log(JSON.stringify(row));
    const lowest = {};
    for (const row of rows) if (row.result !== 'drawn') {
      const key = row.build + ' ' + row.shape;
      if (!(key in lowest) || row.chars < lowest[key].chars) lowest[key] = row;
    }
    console.log('# lowest length that did not draw, per build and shape (label characters inside the link):');
    for (const [key, row] of Object.entries(lowest)) console.log(`#   ${key}: ${row.chars} (${row.result})`);
    for (const lbl of new Set(rows.map(r => r.build)))
      for (const shape of new Set(rows.map(r => r.shape)))
        if (!(`${lbl} ${shape}` in lowest)) console.log(`#   ${lbl} ${shape}: every length drew`);
  })().catch(e => { console.error(e); process.exit(1); });
}
