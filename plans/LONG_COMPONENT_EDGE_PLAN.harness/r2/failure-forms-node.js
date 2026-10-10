// In node (the Node renderer's runtime), which emitter-form edge lengths draw, and what a failure looks like.
const h = require('C:/Code/Kronikol/plans/LONG_COMPONENT_EDGE_PLAN.harness/engine/harness.js');
const out = (s) => process.stdout.write(s + '\n');
const NL = String.fromCharCode(92) + 'n';
function source(len) {
  let flat = ('ClickHouse: ' + Array.from({ length: 2000 }, (_, i) => 'INSERT INTO orders_archive_' + String(i).padStart(4, '0')).join(', ')).slice(0, len);
  const parts = []; let line = '';
  for (const w of flat.split(', ')) { if (line && line.length + 2 + w.length > 100) { parts.push(line + ','); line = w; } else line = line ? line + ', ' + w : w; }
  if (line) parts.push(line);
  return '@startuml\nleft to right direction\nskinparam wrapWidth 200\nrectangle "**Caller**' + NL + '<size:10>[Person]</size>" as caller <<person>>\ndatabase "Warehouse" as warehouse\ncaller -[#E74C3C]-> warehouse : "' + parts.join(NL) + '"\n@enduml';
}
(async () => {
  const dir = process.env.LOCALAPPDATA + '/Kronikol/plantuml-js/1.2026.8/';
  await h.init(dir + 'plantuml.js', dir + 'viz-global.js');
  for (const len of (process.env.LENS || '1500,2500,4000,6000,9000,14000').split(',').map(Number)) {
    const r = await h.render(source(len));
    const svg = r.svg || '';
    const texts = [...svg.matchAll(/<text\b[^>]*>([^<]*)<\/text>/g)].map(m => m[1]);
    out(len + ': verdict=' + r.verdict + ' svg=' + (svg.indexOf('<svg') >= 0) + ' first=' + JSON.stringify((texts[0] || '').slice(0, 40)) + ' last=' + JSON.stringify((texts[texts.length - 1] || r.text || '').slice(-110)));
  }
})().catch(e => { process.stderr.write('ERR ' + (e && e.stack || e) + '\n'); process.exit(1); });
