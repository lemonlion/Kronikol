'use strict';
// node --stack-size=4000 corpus-hash.js <engine.js> <out.json>   (PATCH_LABELS / PATCH_TEAVM from env, via patches.js)
// Renders a corpus (render-bench real/*.puml, probe-component.puml, Labels cases, linked sequence messages, a C4 and
// a class diagram) and writes { file: verdict:sha256 } so two engine variants can be compared byte for byte.
const h = require('./harness'), g = require('./gen'), P = require('./patches');
const fs = require('fs'), path = require('path'), crypto = require('crypto');
const [engine, outFile] = process.argv.slice(2);
const VIZ = 'C:/Users/cex/AppData/Local/Kronikol/plantuml-js/1.2026.8/viz-global.js';
const RB = 'C:/Code/Kronikol/tools/render-bench';
const corpus = {};
for (const f of fs.readdirSync(path.join(RB, 'real'))) if (f.endsWith('.puml')) corpus['real/' + f] = fs.readFileSync(path.join(RB, 'real', f), 'utf8');
corpus['probe-component.puml'] = fs.readFileSync(path.join(RB, 'probe-component.puml'), 'utf8');
for (const [k, e] of Object.entries({
  quoted: 'caller --> warehouse : "ClickHouse: INSERT INTO a, INSERT INTO b - 2 calls across 1 tests"',
  quotedWrapped: 'caller --> warehouse : "' + g.fillLabel(900, 'INSERT INTO orders_archive_001, ', 100) + '"',
  unquoted: 'caller --> warehouse : ClickHouse: INSERT INTO a - 1 calls across 1 tests',
  both: 'caller --> warehouse : "1" uses "n"', first: 'caller --> warehouse : "1" uses', second: 'caller --> warehouse : uses "n"',
  inner: 'caller --> warehouse : "say "hi" now"', curly: 'caller --> warehouse : \u201cleft\u201d mid \u201dright\u201d',
  coloured: 'caller -[#E74C3C]-> warehouse : "HTTP: GET, POST - 252 calls across 241 tests"',
  linked: 'caller --> warehouse : "[[#iflow-rel-caller-warehouse HTTP: GET, POST]]\\nP50: 3ms | P95: 9ms | P99: 12ms\\n4 calls across 2 tests"',
  supplementary: 'caller --> warehouse : "emoji \uD83D\uDE00 and \uD840\uDC00 han"',
})) corpus['edge-' + k] = g.component(e);
corpus['seq-link'] = g.sequence('a -> b : [[#iflow-1 GET /orders?id=1&x=[2] ' + 'y'.repeat(300) + ']]');
corpus['seq-plain'] = g.sequence('a -> b : ' + g.fillLabel(1500));
corpus['seq-creole'] = g.sequence('a -> b : **bold** //it// ""mono"" <color:red>red</color> [[http://x.y/z label]] <&check> \\n line2');
corpus['seq-loop'] = g.shapeSource('loop', 300).source;
corpus['seq-colourbar'] = g.shapeSource('colourbar', 300).source;
corpus['seq-groupbrackets'] = ['@startuml', 'participant a', 'participant b', 'group My Title [the label]', 'a -> b : x', 'end', 'alt ok #lightgreen', 'a -> b : y', 'else fail', 'a -> b : z', 'end', 'loop 3 times', 'a -> b : w', 'end', '@enduml'].join('\n');
corpus['seq-creole-colors'] = ['@startuml', 'participant a', 'participant b', 'a -> b : <color:red>red</color> and <color:#00ff00>green</color> and <color:blue>open to eol', 'note over a : <color:red>multi</color>\\n<color:blue>line</color>', 'hnote across <<stepDelimiter>> #black:<color:white>Given a step with 😀 emoji', 'loop ×3 · <color:red>GET</color> /orders/{id} ' + 'q'.repeat(200), 'a -> b : w', 'end', 'group ×2 [label with <color:blue>colour</color>]', 'a -> b : v', 'end', '@enduml'].join('\n');
corpus['seq-lineterminator'] = ['@startuml', 'participant a', 'participant b', 'hnote across <<stepDelimiter>> #black:<color:white>Given a separator', '@enduml'].join('\n');
corpus['class'] = ['@startuml', 'class Foo<T> {', ' +bar(int x): String', ' -baz: List<String>', '}', 'class Bar', 'Foo "1" *-- "many" Bar : contains >', 'Bar ..|> Foo', '@enduml'].join('\n');
corpus['usecase'] = ['@startuml', 'actor User', '(Login) as L', 'User --> L : "clicks"', ':Admin: --> (Audit)/', '[Comp] ..> () Iface : uses', '@enduml'].join('\n');
corpus['c4'] = g.c4('Rel(caller, warehouse, "' + g.fillLabel(200) + '", $tags="#E74C3C")');

(async () => {
  await h.init(engine, VIZ, P.transform);
  const out = {};
  for (const [k, src] of Object.entries(corpus)) {
    const r = await h.render(src, 120000);
    out[k] = r.verdict + ':' + crypto.createHash('sha256').update(r.svg || '').digest('hex').slice(0, 20) + ':' + (r.svg || '').length;
  }
  out.__gqs = JSON.stringify(globalThis.__gqsStats) + " lazy " + JSON.stringify(globalThis.__rgqsStats);
  fs.writeFileSync(outFile, JSON.stringify(out, null, 1));
  process.exit(0);
})();
