// Renders overflowing statements through the shipped render script (shim page) in Chromium and prints the pictures.
// usage: [JSFLAGS=...] [LEN=n] [KINDS=a,b] node stackpic-browser.js <shim page>
const path = require('path'), url = require('url'), fs = require('fs');
const pw = require(path.resolve('C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const shim = process.argv[2];
const out = (s) => process.stdout.write(s + '\n');
const NL = String.fromCharCode(92) + 'n';   // PlantUML's line-break escape, a backslash and an n
(async () => {
  const browser = await pw.chromium.launch({ args: (process.env.JSFLAGS ? ['--js-flags=' + process.env.JSFLAGS] : []) });
  const page = await browser.newPage();
  await page.goto(url.pathToFileURL(shim).href);
  const len = Number(process.env.LEN || 1500);
  const entries = Array.from({ length: 400 }, (_, i) => 'INSERT INTO orders_archive_' + String(i).padStart(3, '0'));
  let flat = 'ClickHouse: ' + entries.join(', ');
  flat = flat.slice(0, len);
  // wrapped every ~100 characters at a comma, as the emitter's WrapLabel does
  const parts = []; let line = '';
  for (const word of flat.split(', ')) {
    if (line && line.length + 2 + word.length > 100) { parts.push(line + ','); line = word; } else line = line ? line + ', ' + word : word;
  }
  if (line) parts.push(line);
  const wrapped = parts.join(NL);
  const cases = {
    simple: '@startuml\nrectangle "A" as a\nrectangle "B" as b\na --> b : "' + flat + '"\n@enduml',
    emitter: '@startuml\nleft to right direction\nskinparam wrapWidth 200\nrectangle "**Caller**' + NL + '<size:10>[Person]</size>" as caller <<person>>\ndatabase "Warehouse" as warehouse\ncaller -[#E74C3C]-> warehouse : "' + wrapped + '"\n@enduml',
    bar: '@startuml\nparticipant A\nparticipant B\nA -> B: x\nhnote across #black:<color:white>' + 'x'.repeat(20000) + '</color>\n@enduml',
  };
  const kinds = (process.env.KINDS || 'simple,emitter').split(',');
  for (const kind of kinds) {
    const src = cases[kind];
    const html = await page.evaluate(async (s) => {
      const id = 'd' + Math.random().toString(36).slice(2);
      const el = document.createElement('div'); el.id = id; document.body.appendChild(el);
      window.plantuml.render(s.split('\n'), id);
      const t0 = Date.now();
      while (Date.now() - t0 < 60000) {
        if (el.innerHTML) { await new Promise(r => setTimeout(r, 200)); return el.innerHTML; }
        await new Promise(r => setTimeout(r, 100));
      }
      return 'TIMEOUT';
    }, src);
    const texts = [...html.matchAll(/<text\b[^>]*>([^<]*)<\/text>/g)].map(m => m[1]).slice(0, 8);
    out(kind + ' (statement ' + src.split('\n').reduce((a, l) => Math.max(a, l.length), 0) + '): svg=' + (html.indexOf('<svg') >= 0) + ' len=' + html.length);
    out('  texts: ' + JSON.stringify(texts.map(t => t.length > 120 ? t.slice(0, 120) + '...' : t)));
    if (html.indexOf('<svg') < 0) out('  raw: ' + JSON.stringify(html.slice(0, 300)));
    fs.writeFileSync('stackpic-browser-' + kind + '.html', html);
  }
  await browser.close();
})().catch(e => { process.stderr.write('ERR ' + (e && e.stack || e) + '\n'); process.exit(1); });
