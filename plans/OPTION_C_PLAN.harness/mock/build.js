// Builds the publishable preview page from a real generated report.
// Usage: node build.js <report.html> <out.html>
// The Artifact publisher wraps the page in its own <!doctype>/<head>/<body> skeleton, so the
// report's structural tags are removed (its head content simply runs at the top of the body),
// a name goes in the first bytes, and the C stylesheet and transform are injected.
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const [src, out] = process.argv.slice(2);
const s = fs.readFileSync(src, 'utf8');
const css = fs.readFileSync(path.join(__dirname, 'c-skin.css'), 'utf8');
const js = fs.readFileSync(path.join(__dirname, 'c-dom.js'), 'utf8');
const layoutJs = fs.readFileSync(path.join(__dirname, 'c-layout.js'), 'utf8');

// Structural tags only count outside <script> and <style>: the report's scripts carry whole HTML
// documents as strings (an embedded page with its own <head> and <body>, the filtered export).
function structuralTags(html) {
  const tags = [];
  const re = /<(\/?)(script|style|html|head|body|title|!doctype)\b[^>]*>/gi;
  let m;
  while ((m = re.exec(html))) {
    const name = m[2].toLowerCase();
    if (!m[1] && (name === 'script' || name === 'style')) {
      const close = html.toLowerCase().indexOf('</' + name, re.lastIndex);
      if (close < 0) throw new Error('unclosed ' + name);
      re.lastIndex = html.indexOf('>', close) + 1;
      continue;
    }
    tags.push({ tag: (m[1] ? '/' : '') + name, start: m.index, end: m.index + m[0].length });
  }
  return tags;
}
const tags = structuralTags(s);
const find = t => {
  const all = tags.filter(x => x.tag === t);
  if (all.length !== 1) throw new Error(t + ' found ' + all.length + ' times outside scripts');
  return all[0];
};
const doctype = find('!doctype'), html = find('html'), head = find('head'), headEnd = find('/head');
const body = find('body'), bodyEnd = find('/body'), htmlEnd = find('/html');
// <title> also appears in the body, inside inline SVG (tooltips); the document's is the one in the head
const inHead = t => tags.filter(x => x.tag === t && x.start > head.end && x.end <= headEnd.start);
if (inHead('title').length !== 1 || inHead('/title').length !== 1) throw new Error('head title not unique');
const titleOpen = inHead('title')[0], titleClose = inHead('/title')[0];
if (!(doctype.start < html.start && html.end <= head.start && headEnd.end <= body.start && body.end <= bodyEnd.start && bodyEnd.end <= htmlEnd.start)) throw new Error('unexpected order');

let headPart = s.slice(head.end, titleOpen.start) + s.slice(titleClose.end, headEnd.start);
let bodyPart = s.slice(body.end, bodyEnd.start);

// Collapsed notes: the one-line preview drops a JSON body's opening brace, and its closing one when the whole body
// fits (a cut body ends in "..." and has no closing brace left to drop). "{}" stays as it is. The report's scripts
// sit in its head, and the report may be written with CRLF line ends.
const previewRx = new RegExp("        var raw = nonGray\\.join\\(' '\\)\\.trim\\(\\);\\r?\\n        if \\(!raw\\) return '';\\r?\\n        if \\(raw\\.length <= 60\\) return raw;", 'g');
const previewNew = "        var raw = nonGray.join(' ').trim();\n        if (!raw) return '';\n        if (raw.charAt(0) === '{') {\n            var body = raw.replace(/^\\{\\s*/, '');\n            var whole = body.replace(/\\s*\\}$/, '');\n            if (whole && whole !== body && whole.length <= 60) return whole;\n            if (whole) raw = body;\n        }\n        if (raw.length <= 60) return raw;";
let previewCount = 0;
const patchPreview = part => part.replace(previewRx, function () { previewCount++; return previewNew; });
headPart = patchPreview(headPart);
bodyPart = patchPreview(bodyPart);
if (previewCount < 1) throw new Error('getNotePreview not found');
console.log('collapsed preview patched', previewCount, 'time(s)');

// Round 11: "note" is PlantUML's word for the boxes beside the arrows, not the reader's. The report's own wording for
// them, as the product would change it: the dropdowns' names and group headings, and the widen button's tooltip.
// Each must be found, so a report that words them differently fails the build instead of keeping "note".
// Round 16 (owner): the boxes are "payloads" to a reader, as the bar's label now says.
const WORDING = [
  ['aria-label="Note payload format" title="Note payload format"', 'aria-label="Payload format" title="Payload format"'],
  ['aria-label="Note width" title="Note width. Wrap: notes wrap at the report\'s note width. Wide: notes widen to fill the diagram."', 'aria-label="Payload width" title="Payload width. Wrap: they wrap at a set width. Wide: they widen to fill the diagram."'],
  ['<optgroup label="Note width">', '<optgroup label="Payload width">'],
  ["'Widen this note to fill the diagram'", "'Widen this box to fill the diagram'"],
  ["'Back to the default note width'", "'Back to the default width'"],
];
for (const [from, to] of WORDING) {
  const n = headPart.split(from).length - 1 + bodyPart.split(from).length - 1;
  if (!n) throw new Error('wording not found: ' + from);
  headPart = headPart.split(from).join(to);
  bodyPart = bodyPart.split(from).join(to);
  console.log('worded', n, 'x', to.slice(0, 40));
}

// Round 17 (owner, item 3): the format dropdown's list gets a heading, as the width dropdown's has ("Payload width").
{
  let n = 0;
  const rx = /(<select class="note-format-select"[^>]*>)([\s\S]*?)(<\/select>)/g;
  const f = part => part.replace(rx, (m, open, options, close) => { n++; return open + '<optgroup label="Payload format">' + options + '</optgroup>' + close; });
  headPart = f(headPart); bodyPart = f(bodyPart);
  if (!n) throw new Error('format dropdown not found');
  console.log('format dropdowns headed', n);
}

// Round 17 (owner, item 27): the truncate dropdown's list gets a heading as well, so all three read alike.
{
  let n = 0;
  const rx = /(<select class="truncate-lines-select"[^>]*>)([\s\S]*?)(<\/select>)/g;
  const f = part => part.replace(rx, (m, open, options, close) => { n++; return open + '<optgroup label="Payload length">' + options + '</optgroup>' + close; });
  headPart = f(headPart); bodyPart = f(bodyPart);
  if (!n) throw new Error('truncate dropdown not found');
  console.log('truncate dropdowns headed', n);
}

// Round 16: the Events and Setup toggles, patched into the report's note script where the product would add them
// (c-round16-notes.js, c-round16-strips.js), and their buttons, in the report's bar when any diagram has events or
// setup and in a scenario's bar when one of its diagrams does. The events filter is the report's own databases
// filter with `queue` for its participant keyword. A collapsed payload that already shows all of itself loses its
// + and ▾ (item 5). Every anchor must be found exactly once, so a report that changed fails the build.
{
  const zlib = require('zlib');
  const patchOnce = (label, rx, fn) => {
    let n = 0;
    const f = part => part.replace(rx, function () { n++; return fn.apply(null, arguments); });
    headPart = f(headPart); bodyPart = f(bodyPart);
    if (n !== 1) throw new Error(label + ': anchor found ' + n + ' times');
  };
  const extractFunction = (text, header) => {
    const at = text.indexOf(header);
    if (at < 0) return null;
    let depth = 0, i = text.indexOf('{', at);
    for (; i < text.length; i++) { if (text[i] === '{') depth++; else if (text[i] === '}' && --depth === 0) break; }
    return text.slice(at, i + 1);
  };
  const dbHeader = 'function stripDatabaseCalls(source) {';
  const dbFn = extractFunction(headPart, dbHeader) || extractFunction(bodyPart, dbHeader);
  if (!dbFn) throw new Error('stripDatabaseCalls not found');
  const KW = '(?:database|collections)';
  if (dbFn.split(KW).length - 1 !== 2) throw new Error('stripDatabaseCalls: expected its keyword twice');
  const evFn = dbFn.replace('function stripDatabaseCalls(source)', 'function stripEventCalls(source)').split(KW).join('queue');
  const setupFn = fs.readFileSync(path.join(__dirname, 'c-round16-strips.js'), 'utf8');
  const notesAdd = fs.readFileSync(path.join(__dirname, 'c-round16-notes.js'), 'utf8');
  // the same two filters, run here, say where the buttons go
  const strips = new Function(evFn + '\n' + setupFn + '\nreturn { stripEventCalls: stripEventCalls, stripSetup: stripSetup };')();

  patchOnce('filters', /\n( {4}function stripDatabaseCalls\(source\) \{)/, (m, fn) => '\n' + notesAdd + '\n' + setupFn + '\n    ' + evFn + '\n\n' + fn);
  patchOnce('flags', /( {4}window\._databasesVisible = (?:true|false);)/, (m, a) => a + '\n    window._eventsVisible = true;\n    window._setupVisible = true;');
  patchOnce('compose', /return applyDatabasesFilter\(\s*applyStepsFilter\(\s*applyAssertionFilter\(built, !!container\._assertionsVisible\),\s*!!container\._stepsVisible\),\s*!!container\._databasesVisible\);/,
    () => 'return kronEventsFilter(applyDatabasesFilter(applyStepsFilter(applyAssertionFilter(kronSetupFilter(built, container), !!container._assertionsVisible), !!container._stepsVisible), !!container._databasesVisible), container);');
  patchOnce('preprocess', /el\._databasesVisible = window\._databasesVisible;(\r?\n\s*)var renderSource = !el\._assertionsVisible \? stripAssertionNotes\(source\) : source;/,
    (m, nl) => 'el._databasesVisible = window._databasesVisible;' + nl + "el._eventsVisible = kronFlag(el, '_eventsVisible');" + nl + "el._setupVisible = kronFlag(el, '_setupVisible');" + nl +
      'var renderSource = kronSetupFilter(source, el);' + nl + 'renderSource = !el._assertionsVisible ? stripAssertionNotes(renderSource) : renderSource;');
  patchOnce('preprocess-events', /(renderSource = !el\._databasesVisible \? stripDatabaseCalls\(renderSource\) : renderSource;)/, (m, a) => a + ' renderSource = kronEventsFilter(renderSource, el);');
  patchOnce('replay', /var cleanFilt = owner\._noteOriginalSource;/, () => 'var cleanFilt = kronSetupFilter(owner._noteOriginalSource, owner);');
  patchOnce('replay-events', /(if \(!owner\._databasesVisible\) cleanFilt = stripDatabaseCalls\(cleanFilt\);)/, (m, a) => a + ' cleanFilt = kronEventsFilter(cleanFilt, owner);');
  patchOnce('exact-map', /(if \(_map\.length === cleanFiltNotes\.length\) filteredToOrigMap = _map;\r?\n\s*\})/,
    (m, a) => a + '\n            if (owner._setupVisible === false || owner._eventsVisible === false) { var _exact = kronExactNoteMap(owner); if (_exact) filteredToOrigMap = _exact.length < ownerNoteBlocks.length ? _exact : null; }');
  patchOnce('whole', /(var longNote = forceIsLong \|\| isLongNote\(contentLines, container\._truncateLines, hdrHidden\);)/,
    (m, a) => a + " var kronWhole = state === 'collapsed' && kronCollapsedIsWhole(contentLines, hdrHidden);");
  patchOnce('plus', /(\/\/ \+ \(plus\) button[^\n]*\n\s*)if \(state === 'collapsed'\) \{/, (m, a) => a + "if (state === 'collapsed' && !kronWhole) {");
  patchOnce('expand', /(\/\/ Bottom-center expand button[^\n]*\n\s*)if \(state === 'collapsed' \|\| state === 'truncated'\) \{/, (m, a) => a + "if ((state === 'collapsed' && !kronWhole) || state === 'truncated') {");
  console.log('note script patched: events, setup, whole collapsed payloads');

  // which diagrams have events or setup, and the scenario each belongs to
  const dataM = /<script[^>]*id="puml-data"[^>]*>([\s\S]*?)<\/script>/.exec(bodyPart) || /<script[^>]*id="puml-data"[^>]*>([\s\S]*?)<\/script>/.exec(headPart);
  if (!dataM) throw new Error('puml-data not found');
  const data = JSON.parse(dataM[1]);
  const has = {};
  let evCount = 0, setCount = 0;
  for (const id of Object.keys(data)) {
    let src;
    try { src = zlib.gunzipSync(Buffer.from(data[id], 'base64')).toString('utf8'); } catch (e) { continue; }
    if (!/!pragma teoz/.test(src)) continue;
    const ev = strips.stripEventCalls(src) !== src, st = strips.stripSetup(src) !== src;
    if (ev) evCount++;
    if (st) setCount++;
    has[id] = { ev, st };
  }
  const opens = [], scen = [];
  const posRx = /<details\b[^>]*\bclass="([^"]*)"|\bid="(puml-\d+)"/g;
  let pm;
  while ((pm = posRx.exec(bodyPart))) {
    if (pm[1] !== undefined) { if (pm[1].split(/\s+/).includes('scenario')) { opens.push(pm.index); scen.push({ ev: false, st: false }); } }
    else if (scen.length && has[pm[2]]) { scen[scen.length - 1].ev = scen[scen.length - 1].ev || has[pm[2]].ev; scen[scen.length - 1].st = scen[scen.length - 1].st || has[pm[2]].st; }
  }
  const scenarioAt = at => { let lo = 0, hi = opens.length - 1, r = -1; while (lo <= hi) { const mid = (lo + hi) >> 1; if (opens[mid] <= at) { r = mid; lo = mid + 1; } else hi = mid - 1; } return r; };
  const btn = (name, label, handler) => '<button class="details-radio-btn toggle-btn details-active" data-toggle="' + name + '" data-shown="true" onclick="window.' + handler + '(this)">' + label + ' Shown</button>';
  let scenarioButtons = 0;
  bodyPart = bodyPart.replace(/<button class="details-radio-btn toggle-btn[^"]*" data-toggle="databases" data-shown="(?:true|false)" onclick="window\._toggleScenarioDatabases\(this\)">Databases (?:Shown|Hidden)<\/button>/g, (m, offset) => {
    const s = scen[scenarioAt(offset)];
    if (!s) return m;
    let add = '';
    if (s.ev) { add += btn('events', 'Events', '_toggleScenarioEvents'); scenarioButtons++; }
    if (s.st) { add += btn('setup', 'Setup', '_toggleScenarioSetup'); scenarioButtons++; }
    return m + add;
  });
  let reportButtons = 0;
  bodyPart = bodyPart.replace(/<button class="details-radio-btn toggle-btn[^"]*" data-toggle="databases" data-shown="(?:true|false)" onclick="window\._toggleDatabases\(this\)">Databases (?:Shown|Hidden)<\/button>/, m => {
    let add = '';
    if (evCount) { add += btn('events', 'Events', '_toggleEvents'); reportButtons++; }
    if (setCount) { add += btn('setup', 'Setup', '_toggleSetup'); reportButtons++; }
    return m + add;
  });
  console.log('diagrams with events', evCount, 'with setup', setCount, '| scenario buttons', scenarioButtons, '| report buttons', reportButtons, '| scenarios', scen.length);
}

// ENGINE=<plantuml.js> swaps the engine the page loads for a local build: the text rides in the page, a Blob URL
// stands in for the CDN address, and the shim's integrity check is given the build's own hash.
let engineScript = '';
if (process.env.ENGINE) {
  const text = fs.readFileSync(process.env.ENGINE, 'utf8');
  if (!/export\s*\{\s*[\w$]+\s+as\s+render\s*,\s*[\w$]+\s+as\s+renderToString\s*\}\s*;?\s*$/.test(text.slice(-300)))
    throw new Error('engine does not end in the render/renderToString export the shim expects');
  const sri = 'sha256-' + crypto.createHash('sha256').update(Buffer.from(text, 'utf8')).digest('base64');
  engineScript = '<script id="kronikol-engine">window.__kronikolEngineUrl = URL.createObjectURL(new Blob([' +
    JSON.stringify(text).replace(/</g, String.fromCharCode(92) + 'u003c') + '], { type: "text/javascript" }));</script>' + String.fromCharCode(10);
  let swapped = 0;
  const swap = part => part
    .replace(/var ENGINE_URL = ('https:\/\/cdn\.jsdelivr\.net\/npm\/@plantuml\/core@[^']+\/plantuml\.js');/, (m, url) => { swapped++; return 'var ENGINE_URL = window.__kronikolEngineUrl || ' + url + ';'; })
    .replace(/var ENGINE_INTEGRITY = 'sha256-[A-Za-z0-9+\/=]+';/, () => { swapped++; return "var ENGINE_INTEGRITY = '" + sri + "';"; });
  headPart = swap(headPart);
  bodyPart = swap(bodyPart);
  if (swapped !== 2) throw new Error('engine swap matched ' + swapped + ' of 2 lines');
  console.log('engine', process.env.ENGINE, sri);
}

// Round 17 (owner, item 21): the diagram menu's "open in new tab" items open the tab while the click is handled and
// write the picture or source into it (c-round17.js), so a popup blocker lets it through and no blob: URL is navigated.
{
  let n = 0;
  const swaps = [
    [/svgToCanvas\(svg, function\(canvas\) \{\s*canvas\.toBlob\(function\(blob\) \{\s*window\.open\(URL\.createObjectURL\(blob\)\);\s*\}, 'image\/png'\);\s*\}\);/g,
      "(function (w) { svgToCanvas(svg, function(canvas) { window.__kOpenPng(w, canvas); }); })(window.__kOpenTab());"],
    [/svgToCanvasWithBg\(svg, function\(canvas\) \{\s*canvas\.toBlob\(function\(blob\) \{\s*window\.open\(URL\.createObjectURL\(blob\)\);\s*\}, 'image\/png'\);\s*\}\);/g,
      "(function (w) { svgToCanvasWithBg(svg, function(canvas) { window.__kOpenPng(w, canvas); }); })(window.__kOpenTab());"],
    [/var blob = new Blob\(\[serializeSvg\(svg\)\], \{ type: 'image\/svg\+xml' \}\);\s*window\.open\(URL\.createObjectURL\(blob\)\);/g,
      "window.__kOpenSvg(serializeSvg(svg));"],
    [/var blob = new Blob\(\[(\w+)\], \{ type: 'text\/plain;charset=utf-8' \}\);\s*window\.open\(URL\.createObjectURL\(blob\)\);/g,
      "window.__kOpenText($1);"],
  ];
  const apply = text => { for (const [rx, to] of swaps) text = text.replace(rx, () => { n++; return to.replace('$1', '__ARG__'); }); return text; };
  // the text swap keeps its argument
  const applyAll = text => {
    for (const [rx, to] of swaps) text = text.replace(rx, (m, a) => { n++; return a ? to.replace('$1', a) : to; });
    return text;
  };
  headPart = applyAll(headPart); bodyPart = applyAll(bodyPart);
  if (n < 6) throw new Error('open-in-new-tab items found ' + n + ' times');
  console.log('open-in-new-tab items swapped ' + n);
}

// Round 17 (owner, item 10): flame bars carry a palette hue (--fh) in place of a random hue faded by depth.
{
  let n = 0;
  const from = "background:hsl(' + hue + ', 60%, ' + lightness\n            + '%)\"";
  const swap = text => {
    const at = text.indexOf(from);
    if (at < 0) return text;
    n++;
    text = text.slice(0, at) + "--fh:' + FLAME_HUES[Math.abs(hashCode(source)) % FLAME_HUES.length] + '\"" + text.slice(at + from.length);
    return text.replace('function buildBar(source, name, leftPct, widthPct, depth, durMs, totalDurMs) {',
      '// Round 17 (owner, item 10): eight hues that sit with the diagrams (blue, violet, teal, rose, green, amber, purple, coral)\n' +
      '    var FLAME_HUES = [212, 262, 188, 338, 152, 32, 290, 8];\n' +
      '    function buildBar(source, name, leftPct, widthPct, depth, durMs, totalDurMs) {');
  };
  headPart = swap(headPart); bodyPart = swap(bodyPart);
  if (n !== 1) throw new Error('flame bar colour found ' + n + ' times');
  console.log('flame palette swapped');
}

// Round 17 (owner, items 13-15): the page's note buttons, as round 16 left them, with the top-right buttons packed
// side by side (no gap where a hidden button would be), the triangles drawn 2px clear of their button's edges, and
// a truncated note's bottom holding both triangles. The whole function is swapped, so it must be found once.
{
  const header = 'function createNoteButtons(svg, bbox, noteStep,';
  const replacement = fs.readFileSync(path.join(__dirname, 'c-round17-buttons.js'), 'utf8');
  let n = 0;
  const swapFn = text => {
    const at = text.indexOf(header);
    if (at < 0) return text;
    let depth = 0, i = text.indexOf('{', at);
    for (; i < text.length; i++) { if (text[i] === '{') depth++; else if (text[i] === '}' && --depth === 0) break; }
    n++;
    return text.slice(0, at) + replacement + text.slice(i + 1);
  };
  headPart = swapFn(headPart); bodyPart = swapFn(bodyPart);
  if (n !== 1) throw new Error('createNoteButtons found ' + n + ' times');
  console.log('note buttons swapped');
}

const title = '<title>Option C Report Preview</title>\n';
const style = '<style id="kronikol-c">\n' + css + '\n</style>\n';
const note = '<div class="kron-preview-note"><b>Option C preview.</b> A real Kronikol 4.26.4 report ' +
  '(BreakfastProvider ReqNRoll suite, 214 scenarios, run locally) with the C stylesheet and layout applied in the browser. ' +
  'For the preview the suite was run five times, labelled as BreakfastProvider CI runs #318 to #322, with an assertion and ' +
  'an example row edited to fail, so History holds real verdicts.' +
  (process.env.ENGINE_LABEL ? ' Diagrams are drawn by ' + process.env.ENGINE_LABEL + ', embedded in this page.' : '') + '</div>\n';
const script = '<script id="kronikol-c-dom">\n' + js + '\n</script>\n<script id="kronikol-c-layout">\n' + layoutJs + '\n</script>\n';

// Light or dark before the first paint: the reader's stored choice, else the system's.
const themeBoot = '<script id="kronikol-theme-boot">(function(){var t=null;try{t=localStorage.getItem("kronikol.theme");}catch(e){}' +
  'if(t!=="dark"&&t!=="light")t=(window.matchMedia&&window.matchMedia("(prefers-color-scheme: dark)").matches)?"dark":"light";' +
  'document.documentElement.setAttribute("data-theme",t);})();</script>' + String.fromCharCode(10);
const page = title + themeBoot + engineScript + headPart + style + note + bodyPart + script;
fs.writeFileSync(out, page);
console.log(out, (page.length / 1048576).toFixed(2) + ' MB');

// Local test copy: the same skeleton the publisher adds, so the page renders in standards mode.
const skel = '<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">' +
  '<style>:root{color-scheme:light;padding-top:env(safe-area-inset-top,0px);padding-bottom:env(safe-area-inset-bottom,0px)}body{margin:0;font:14px system-ui,sans-serif}img{max-width:100%}[hidden]{display:none!important}</style></head><body>';
fs.writeFileSync(out.replace(/\.html$/, '.local.html'), skel + page + '</body></html>');
