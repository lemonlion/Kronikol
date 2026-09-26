// statement-limits-worker-probe.js: PlantUmlStatementLimits measured where BrowserJs renders, in the shipped
// worker, instead of in node (statement-limits-probe.js). The page is the real render script with one worker
// (emitter-corpus -- --shim <page> 1); the engine comes from each given CDN base.
//   node statement-limits-worker-probe.js <shim page> <label>=<cdn base> [...]
// Message sources follow plans/DIAGRAM_COLOURS_PLAN.harness/statement-node.js (its F25, R30): the emitter's
// capped request statement for a 2,300-character query, cut to each length, with and without its
// [[#iflow-…]] link. Block labels and coloured bars carry the same query text.
const fs = require('fs'), path = require('path'), url = require('url'), os = require('os');
const pw = require(path.resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
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
  const browser = await pw.chromium.launch();
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
