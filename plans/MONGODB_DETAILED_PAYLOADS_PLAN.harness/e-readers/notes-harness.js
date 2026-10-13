// Loads src/Kronikol/Reports/collapsible-notes-script.js (read-only copy of the source) with DOM stubs,
// then exercises the exposed pure JSON/YAML helpers.
'use strict';
const fs = require('fs');
const vm = require('vm');

const src = fs.readFileSync(require('path').resolve(__dirname, '../../..', 'src/Kronikol/Reports/collapsible-notes-script.js'), 'utf8')
  .replace(/^<script>/, '').replace(/<\/script>\s*$/, '')
  .replace(/__HEADERS_HIDDEN_DEFAULT__/g, 'false')
  .replace(/__TRUNCATE_LINES_DEFAULT__/g, '40')
  .replace(/__ASSERTIONS_VISIBLE_DEFAULT__/g, 'true')
  .replace(/__STEPS_VISIBLE_DEFAULT__/g, 'true')
  .replace(/__DATABASES_VISIBLE_DEFAULT__/g, 'true');

function stub() {
  const f = function () { return stub(); };
  return new Proxy(f, {
    get(t, p) {
      if (p === Symbol.toPrimitive) return () => '';
      if (p === 'length') return 0;
      if (p === Symbol.iterator) return function* () {};
      if (p === 'then') return undefined;
      return stub();
    },
    apply() { return stub(); },
    construct() { return stub(); },
  });
}
const win = {};
const ctx = vm.createContext({ window: win, document: stub(), console, setTimeout, clearTimeout, MutationObserver: function () { return stub(); }, navigator: stub(), getComputedStyle: () => stub() });
vm.runInContext(src, ctx);
const I = win._noteFormatInternals;

console.log('=== Q5: splitNoteCapFootnote (JS) ===');
for (const s of ['[1,2]\n... (3 more)', '[1,2]\n... (3 more documents not shown)', '{"a":1}\n... (3 more)', '[1,2]\n... (3 more rows not shown)', '[1,2]\n... (3 more statements not shown)']) {
  const r = I.splitNoteCapFootnote(s.split('\n'));
  console.log(JSON.stringify(s), '=>', JSON.stringify(r));
}

console.log('\n=== Q6: YAML view of {"big":9007199254740993,"d":1.0,"s":"José <b>"} ===');
// The note text the generator would write: TryFormatAsJson indents it, EscapeCreoleMarkup writes `<b>` as `~<b>`.
const noteLines = ['{', '  "big": 9007199254740993,', '  "d": 1.0,', '  "s": "José ~<b>"', '}'];
const json = I.reconstructNoteJson(noteLines);
console.log('reconstructNoteJson ->', JSON.stringify(json));
const yaml = I.jsonTextToYamlLines(json);
console.log('jsonTextToYamlLines ->', JSON.stringify(yaml.map(l => l.t)));
console.log('escapeYamlLinesForNote ->', JSON.stringify(I.escapeYamlLinesForNote(yaml)));
console.log('displayed (unescapeNoteDisplayLine) ->', JSON.stringify(I.escapeYamlLinesForNote(yaml).map(I.unescapeNoteDisplayLine)));
console.log('for contrast, JSON.parse(...) value ->', JSON.stringify(JSON.parse(json)));

console.log('\n=== Q6 extra: one-line compact input straight (no generator re-indent) ===');
const compact = I.reconstructNoteJson(['{"big":9007199254740993,"d":1.0,"s":"José ~<b>"}']);
console.log(JSON.stringify(I.jsonTextToYamlLines(compact).map(l => l.t)));

console.log('\n=== Q6 extra: extended JSON wrappers ===');
const ext = ['{', '  "_id": {', '    "$oid": "65a1b2c3d4e5f6a7b8c9d0e1"', '  },', '  "amount": {', '    "$numberDecimal": "1000.50"', '  },', '  "at": {', '    "$date": "2024-01-01T00:00:00Z"', '  },', '  "u": {', '    "$uuid": "0f8fad5b-d9cb-469f-a165-70867728950e"', '  },', '  "n": 9223372036854775807', '}', '... (3 more)'];
const sp = I.splitNoteCapFootnote(ext);
console.log('footnote:', JSON.stringify(sp.footnote));
console.log(JSON.stringify(I.jsonTextToYamlLines(I.reconstructNoteJson(sp.lines)).map(l => l.t)));
