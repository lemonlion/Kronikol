const fs = require('fs');
const { JSDOM } = require('jsdom');
const src = fs.readFileSync('C:/Code/Kronikol/src/Kronikol/Reports/report-export-function.js', 'utf8');

// A report body shaped like the real one: one visible feature, one hidden, #puml-data, and a
// candidate #history-data in two placements — direct child of body, and nested inside a div.
const html = `<html><head><title>t</title></head><body>
<details class="feature" id="f1"><summary>Visible</summary><div id="d1">x</div></details>
<details class="feature" id="f2" style="display:none"><summary>Hidden</summary><div id="d2">y</div></details>
<section id="history-section">the aggregate History section</section>
<div id="wrapper"><script id="history-data-nested" type="application/json">{"nested":1}</script></div>
<script id="history-data" type="application/json">{"a1b2":["P","P","F"]}</script>
<script id="puml-data" type="application/json">{"d1":"KEPT","d2":"PRUNED"}</script>
</body></html>`;

const dom = new JSDOM(html, { runScripts: 'outside-only' });
const w = dom.window;
// capture what export_html hands to the download, without touching the function under test
w.eval(`var __captured = null;
        function Blob(parts){ __captured = parts.join(''); }
        var URL = { createObjectURL: function(){ return 'blob:x'; }, revokeObjectURL: function(){} };`);
w.eval(src);
// the export walks the *visible* features; stub fc() the way the report supplies it
w.eval(`function fc(){ return { features: Array.from(document.querySelectorAll('details.feature')) }; }`);
w.eval('export_html()');
const out = w.eval('__captured');

const has = (s) => out.includes(s);
console.log('#history-data (direct child of body) copied :', has('id="history-data"'));
console.log('  ...with its payload intact                :', has('"a1b2"'));
console.log('#history-data-nested (inside a div) copied  :', has('history-data-nested'));
console.log('#puml-data pruned to visible ids            :', has('KEPT') && !has('PRUNED'));
console.log('hidden feature excluded                     :', !has('id="f2"'));
console.log('History SECTION carried over                :', has('history-section'));
console.log('head carried over                           :', has('<title>t</title>'));
