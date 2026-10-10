// statement-limits-worker-probe.js: PlantUmlStatementLimits measured where BrowserJs renders, in the shipped
// worker, instead of in node (statement-limits-probe.js). The page is the real render script with one worker
// (emitter-corpus -- --shim <page> 1; 0 workers measures the page's main thread); the engine comes from each given
// CDN base. Every mode reads its sources from the emitter, in the form the emitter writes them, and every verdict
// is strict: `drawn` is an SVG that is not the engine's stack-overflow picture (RangeError, "Maximum call stack",
// "too much recursion"), not its error picture ("Syntax Error?", "An error has occurred", or any picture whose first
// drawn line starts `PlantUML `, which is how the engine's error pictures begin), and that holds every name the
// diagram must show, a sequence diagram's participants twice (head and foot box), since an over-long statement can
// also send the parser to the class-diagram fallback, which echoes the text and draws no error.
//
//   node statement-limits-worker-probe.js --kinds <kinds dir> <shim page> <label>=<cdn base> [...]
// plans/LONG_COMPONENT_EDGE_PLAN.md R3: the statements other than messages and links whose length a run controls,
// from emitter-corpus -- --statement-kinds <dir>: the collapsed-run `loop` and the setup `partition` openers
// lengthened to each length, the coloured step bar of a one-token step cut to each length, and one source per length
// for a participant's name (sequence: an HTTP service and a database; component: a database and a <<system>>
// rectangle), a span name and a swimlane in the internal-flow activity diagram, and the placeholder note's message.
// KINDS=<comma list> picks some. Before 4.14.5 this mode was the default and counted any SVG as drawn, error pictures
// included, and lengthened the bar with text holding display breaks the emitter never writes in that form: loop
// labels that drew `Syntax Error?` and coloured bars that failed from 880 characters read as drawn to 2,000.
//
//   node statement-limits-worker-probe.js --scan <linked dir> <shim page> <label>=<cdn base> [...]
// plans/ENGINE_PIN_PLAN.md S0: the text inside the internal-flow link, per label shape, cut the way
// PlantUmlStatementLimits.TruncateLabel cuts it. The sources come from emitter-corpus -- --linked-labels <dir>.
// UNLINKED=1: the same label without its link, as the emitter writes it with internal-flow tracking off.
//
//   node statement-limits-worker-probe.js --scan-edges <edges dir> <shim page> <label>=<cdn base> [...]
// plans/LONG_COMPONENT_EDGE_PLAN.md R1: the component diagram's edge label, per shape, from
// emitter-corpus -- --component-edges <dir>: the label cut to each length with TruncateLabel (only its length and its
// quote characters matter to the engine), the stats form cut inside its link with the stats lines kept. `chars` is
// the label as written, the quotes around it not counted.
//
// Every mode scans each shape from FROM to TO in steps of STEP (defaults per mode), or with BISECT=1 bisects between
// them to STEP and repeats each verdict at the edge REPS times (default 2). ASIS=1 renders each source as the emitter
// wrote it (the check after a fix). COLD=1 gives every case a fresh page, so its render is the first in a new
// worker (CONCURRENCY=<n> pages at once); without it the cases share one page, after WARM=<n> warm-up renders
// (default 0). JSFLAGS goes to Chromium as --js-flags ("--no-opt --no-maglev": interpreter and baseline frames only,
// as a browser runs with its optimizing compilers off). BROWSER=chromium|chrome|firefox|webkit (chrome is the
// installed Chrome, Playwright's chrome channel), LAUNCH=<json> is merged into the launch options. BENCH_PW points
// at a Playwright package (default: the end-to-end project's).
const fs = require('fs'), path = require('path'), url = require('url'), os = require('os');
const pw = require(process.env.BENCH_PW || path.resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));

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
// A statement cut to exactly n characters, without stranding the backslash of a two-character escape.
function cutAt(text, n) {
  let s = text.slice(0, n), slashes = 0;
  while (slashes < s.length && s[s.length - 1 - slashes] === '\\') slashes++;
  return slashes % 2 === 1 ? s.slice(0, -1) + 'x' : s;
}
const readSource = file => fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n');
function replaceLine(src, rx, line) {
  const lines = src.split('\n'), i = lines.findIndex(l => rx.test(l));
  if (i < 0) throw new Error('no line ' + rx);
  lines[i] = line;
  return lines.join('\n');
}
// Filler for the lengthened openers: SQL text, as the long values Kronikol writes are. Its content does not matter
// to the engine but for quote characters, which end the run it walks.
const FILLER = 'SELECT o.Id, o.CustomerId, o.Total, o.Status, l.Sku FROM Orders AS o INNER JOIN OrderLines AS l ON l.OrderId = o.Id WHERE o.CustomerId = @p0 ';
const filler = n => FILLER.repeat(Math.ceil(n / FILLER.length) + 1).slice(0, n);
// The names a diagram must draw: each participant declared `<shape> "<name>" as <alias>` (the first line of a
// wrapped name, without its bold markers), once in a component diagram, twice in a sequence diagram.
function declaredNames(src, times) {
  const names = [];
  for (const m of src.matchAll(/^\s*(?:participant|actor|entity|database|collections|queue|rectangle|boundary|control)\s+"(.*?)"\s+as\s+\S+/gm)) {
    const first = m[1].split('\\n')[0].replace(/\*\*/g, '').trim();
    if (first) names.push([first.slice(0, 24), times]);
  }
  return names;
}

function linkedShapes(dir) {
  return fs.readdirSync(dir).filter(f => /^linked-.*\.puml$/.test(f)).sort().map(file => {
    const source = readSource(path.join(dir, file));
    const statement = source.split('\n').find(l => l.includes('[[#iflow-'));
    // The request arrow, `a -> b: [[#iflow-<id> <label>]]`, or a component edge, `a --> b : "[[#iflow-rel-… <methods>]]\n…"`.
    const m = /^(.*?)\[\[#(iflow-[^\s\]]+) (.*?)\]\](.*)$/.exec(statement);
    if (!m) throw new Error(file + ': no linked statement');
    const full = m[3].replace(/…$/, '');
    const sequence = source.includes('!pragma teoz');
    return {
      shape: file.slice(7, -5), sequence, max: full.length, source,
      expect: sequence ? [['OrderService', 2]] : declaredNames(source, 1),
      at: n => {
        const text = truncateLabel(full, n);
        const cut = process.env.UNLINKED === '1' ? `${m[1]}${text}${m[4]}` : `${m[1]}[[#${m[2]} ${text}]]${m[4]}`;
        return { src: source.replace(statement, cut), chars: text.length, statement: cut.trim().length };
      },
    };
  });
}

function edgeShapes(dir) {
  return fs.readdirSync(dir).filter(f => /^edge-.*\.puml$/.test(f)).sort().map(file => {
    const source = readSource(path.join(dir, file));
    const statement = source.split('\n').find(l => / : "/.test(l) && /-\[#|-->|\.\.>/.test(l));
    const m = /^(.*? : ")(.*)(")$/.exec(statement);
    if (!m) throw new Error(file + ': no edge');
    const label = m[2];
    // The stats form: the method list inside `[[#iflow-rel-… <methods>]]`, then the stats lines.
    const link = /^(\[\[#iflow-rel-\S+ )(.*?)(\]\].*)$/.exec(label);
    return {
      shape: file.slice(5, -5), sequence: false, source, expect: declaredNames(source, 1),
      max: link ? link[1].length + link[2].replace(/…$/, '').length + link[3].length : label.replace(/…$/, '').length,
      at: n => {
        const cut = link
          ? link[1] + truncateLabel(link[2].replace(/…$/, ''), n - link[1].length - link[3].length) + link[3]
          : truncateLabel(label.replace(/…$/, ''), n);
        return { src: source.replace(statement, m[1] + cut + m[3]), chars: cut.length, statement: (m[1] + cut + m[3]).trim().length };
      },
    };
  });
}

function kindShapes(dir) {
  const pick = process.env.KINDS ? new Set(process.env.KINDS.split(',')) : null;
  const shapes = [];
  const add = s => { if (!pick || pick.has(s.shape)) shapes.push(s); };
  for (const [shape, rx, prefix, expect] of [
    ['loop', /^loop /, 'loop ', [['Api', 2], ['StockService', 2], ['OrderService', 2]]],
    ['partition', /^partition /, 'partition #F6F6F6 ', [['Api', 2], ['CustomerService', 2], ['OrderService', 2]]],
  ]) {
    const source = readSource(path.join(dir, shape + '.puml'));
    add({ shape, sequence: true, source, expect, max: 2000, at: n => {
      const line = prefix + filler(n - prefix.length);
      return { src: replaceLine(source, rx, line), chars: line.length, statement: line.length };
    } });
  }
  {
    const source = readSource(path.join(dir, 'bar-token.puml'));
    const bar = source.split('\n').find(l => /^hnote across/.test(l));
    add({ shape: 'bar-token', sequence: true, source, expect: [['Api', 2], ['OrderService', 2]], max: bar.length, at: n => {
      const line = cutAt(bar, n);
      return { src: replaceLine(source, /^hnote across/, line), chars: line.length, statement: line.length };
    } });
  }
  const name0 = 'Svcorders-archive-replica.eu-west-1.inte', sql0 = 'SELECT o.Id, o.CustomerId, o.Total';
  const perLength = {
    'seq-entity': [true, v => [['Api', 2], [name0.slice(0, Math.min(24, v)), 2]]],
    'seq-database': [true, v => [['Api', 2], [name0.slice(0, Math.min(24, v)), 2]]],
    'comp-database': [false, v => [['Caller', 1], [name0.slice(0, Math.min(24, v)), 1]]],
    'comp-system': [false, v => [['Caller', 1], ['StockService', 1], [name0.slice(0, Math.min(24, v)), 1]]],
    activity: [false, v => [['POST /orders', 1], [sql0.slice(0, Math.min(24, v)), 1]]],
    'activity-lane': [false, v => [['POST /orders', 1], ['SELECT 1', 1], [name0.slice(0, Math.min(24, v)), 1]]],
    placeholder: [false, () => [['diagram could not be generated', 1]]],
  };
  for (const [shape, [sequence, expect]] of Object.entries(perLength)) {
    const sub = path.join(dir, shape);
    if (!fs.existsSync(sub)) continue;
    const lengths = fs.readdirSync(sub).map(f => +f.replace(/\.puml$/, '')).filter(Number.isFinite).sort((a, b) => a - b);
    const nearest = n => lengths.reduce((a, b) => Math.abs(b - n) < Math.abs(a - n) ? b : a);
    add({ shape, sequence, max: lengths[lengths.length - 1], source: readSource(path.join(sub, lengths[lengths.length - 1] + '.puml')), expect: expect(1000),
      at: n => {
        const k = nearest(n), src = readSource(path.join(sub, k + '.puml'));
        // The longest statement the emitter wrote for this value, for the record.
        const longest = src.split('\n').reduce((a, l) => l.trim().length > a ? l.trim().length : a, 0);
        return { src, chars: k, statement: longest, expect: expect(k) };
      } });
  }
  return shapes;
}

// The verdict, run in the page against the element the diagram is drawn into.
const CLASSIFY = `(function (el, expect) {
  if (!el) return null;
  var svg = el.querySelector('svg');
  if (svg) {
    var texts = Array.prototype.map.call(svg.querySelectorAll('text'), function (t) { return t.textContent; });
    var all = (svg.textContent || '').replace(/\\u00a0/g, ' ');
    var first = (texts[0] || '').trim();
    if (/RangeError|Maximum call stack|too much recursion/i.test(all)) return { v: 'STACK-PICTURE', detail: first.slice(0, 60) };
    if (/Syntax Error\\?|An error has occurred/i.test(all) || first.indexOf('PlantUML ') === 0) return { v: 'ERROR-PICTURE', detail: first.slice(0, 60) };
    var missing = [];
    for (var i = 0; i < expect.length; i++) {
      var n = all.split(expect[i][0]).length - 1;
      if (n < expect[i][1]) missing.push(expect[i][0].slice(0, 16) + ' ' + n + '/' + expect[i][1]);
    }
    return missing.length ? { v: 'MISSING', detail: missing.join(', ') } : { v: 'drawn' };
  }
  var text = (el.textContent || '').trim();
  if (text) return { v: /Maximum call stack|too much recursion|RangeError/i.test(text) ? 'STACK' : 'TEXT', detail: text.slice(0, 120) };
  return null;
})`;

function run(shapes, defaults, rest) {
  const [shim, ...bases] = rest;
  if (!shim || bases.length === 0) throw new Error('give the shim page and at least one <label>=<cdn base>');
  const from = +(process.env.FROM || defaults.from), to = +(process.env.TO || defaults.to), step = +(process.env.STEP || defaults.step);
  const reps = +(process.env.REPS || 2), warm = +(process.env.WARM || 0);
  const browserName = process.env.BROWSER || 'chromium';
  const cold = process.env.COLD === '1', concurrency = +(process.env.CONCURRENCY || 1);
  const launchArgs = process.env.JSFLAGS ? ['--js-flags=' + process.env.JSFLAGS] : [];
  const shimHtml = fs.readFileSync(shim, 'utf8');
  // The CDN base the shim page was written with: TrackingDefaults.PlantUmlJsCdnBase of the build that wrote it.
  const pinned = process.env.PINNED || /PlantUmlJsCdnBase = "([^"]+)"/.exec(fs.readFileSync(path.resolve(__dirname, '../../src/Kronikol/Constants/TrackingDefaults.cs'), 'utf8'))[1];
  if (!shimHtml.includes(pinned)) throw new Error('the shim page does not carry ' + pinned + '; set PINNED to the base it was written with');
  const openPage = async (browser, pagePath) => {
    const page = await browser.newPage();
    await page.goto(url.pathToFileURL(pagePath).href);
    await page.waitForFunction(() => window.__kronikolRender && window.__kronikolRender.engineReadyAt !== null
      || (document.body && document.body.textContent.indexOf('Render error') >= 0), null, { timeout: 180000, polling: 200 });
    return page;
  };
  let serial = 0;
  const renderOn = (page, src, expect) => page.evaluate(([src, id, classify, expect]) => new Promise(resolve => {
    const el = document.createElement('div'); el.id = id; document.body.appendChild(el);
    const cls = (0, eval)(classify);
    const done = v => { clearInterval(t); clearTimeout(to); el.remove(); resolve(v); };
    const t = setInterval(() => { const r = cls(el, expect); if (r) done(r); }, 25);
    const to = setTimeout(() => done({ v: 'TIMEOUT' }), 90000);
    window.plantuml.render(src.split('\n'), id);
  }), [src, 'p' + (++serial), CLASSIFY, expect]);

  (async () => {
    const extra = process.env.LAUNCH ? JSON.parse(process.env.LAUNCH) : {};
    const browserType = browserName === 'chrome' ? pw.chromium : pw[browserName];
    const options = Object.assign(browserName === 'chromium' || browserName === 'chrome' ? { args: launchArgs } : {},
      browserName === 'chrome' ? { channel: 'chrome' } : {}, extra);
    const browser = await browserType.launch(options);
    const rows = [], summaries = [];
    for (const arg of bases) {
      const i = arg.indexOf('='), lbl = arg.slice(0, i), cdn = arg.slice(i + 1);
      const pagePath = path.join(os.tmpdir(), `statement-probe-${lbl}-${browserName}-${process.pid}.html`);
      fs.writeFileSync(pagePath, shimHtml.split(pinned).join(cdn));
      const shared = await openPage(browser, pagePath);
      const mode = await shared.evaluate(() => window.__kronikolRender.mode + (window.__kronikolRender.fallbackReason ? ' (' + window.__kronikolRender.fallbackReason + ')' : ''));
      if (!cold && warm > 0) {
        // Warm-up: the first shape's source as written, cut to a third of the range's start so it draws.
        const s = shapes[0], c = s.at(Math.max(10, Math.floor(from / 3)));
        for (let k = 0; k < warm; k++) await renderOn(shared, c.src, c.expect || s.expect);
      }
      const measure = async (s, n, tag) => {
        const c = process.env.ASIS === '1' ? { src: s.source, chars: n, statement: n } : s.at(n);
        let r;
        if (cold) { const pg = await openPage(browser, pagePath); try { r = await renderOn(pg, c.src, c.expect || s.expect); } finally { await pg.close(); } }
        else r = await renderOn(shared, c.src, c.expect || s.expect);
        const row = { build: lbl, shape: s.shape, n, chars: c.chars, statement: c.statement, tag, result: r.v, detail: r.detail };
        rows.push(row);
        if (process.env.VERBOSE === '1') console.log(JSON.stringify(row));
        return row;
      };
      const jobs = shapes.map(s => async () => {
        if (process.env.ASIS === '1') { await measure(s, s.max, 'asis'); return; }
        const hi = Math.min(to, s.max);
        if (process.env.BISECT !== '1') {
          for (let n = from; n <= hi; n += step) await measure(s, n, 'scan');
          return;
        }
        const results = {};
        const at = async (n, tag) => { const row = await measure(s, n, tag); (results[n] = results[n] || []).push(row.result); return row.result === 'drawn'; };
        if (await at(hi, 'hi')) {
          for (let k = 1; k < reps; k++) await at(hi, 'hi-rep');
          summaries.push({ build: lbl, shape: s.shape, everyLengthDrewTo: hi, results });
          return;
        }
        let lo = Math.max(step, from), up = hi;
        while (!(await at(lo, 'lo'))) {
          if (lo <= step) { summaries.push({ build: lbl, shape: s.shape, nothingDrewFrom: lo, results }); return; }
          up = lo; lo = Math.max(step, Math.round(lo / 2 / step) * step);
        }
        while (up - lo > step) {
          const mid = Math.round((lo + up) / 2 / step) * step;
          if (mid === lo || mid === up) break;
          if (await at(mid, 'mid')) lo = mid; else up = mid;
        }
        for (let k = 1; k < reps; k++) { await at(lo, 'edge-rep'); await at(up, 'edge-rep'); }
        if (lo - step > 0) await at(lo - step, 'around');
        if (up + step <= s.max) await at(up + step, 'around');
        summaries.push({ build: lbl, shape: s.shape, lastDrew: lo, firstFailed: up, results });
      });
      let next = 0;
      await Promise.all(Array.from({ length: cold ? concurrency : 1 }, async () => { while (next < jobs.length) await jobs[next++](); }));
      console.log(`# ${lbl} ${cdn} | ${browserName} ${browser.version()}, ${mode}${cold ? ', a fresh page per case' : ', one page' + (warm ? ` after ${warm} warm-up renders` : '')}${launchArgs.length ? ', ' + launchArgs.join(' ') : ''}, file://, ${os.platform()}`);
      await shared.close();
    }
    await browser.close();
    rows.sort((a, b) => a.build.localeCompare(b.build) || a.shape.localeCompare(b.shape) || a.n - b.n);
    if (process.env.VERBOSE !== '1') for (const row of rows) console.log(JSON.stringify(row));
    for (const s of summaries) console.log('# ' + JSON.stringify(s));
    const lowest = {};
    for (const row of rows) if (row.result !== 'drawn') {
      const key = row.build + ' ' + row.shape;
      if (!(key in lowest) || row.chars < lowest[key].chars) lowest[key] = row;
    }
    console.log('# lowest length that did not draw, per build and shape (`chars`: the cut text as written):');
    for (const [key, row] of Object.entries(lowest)) console.log(`#   ${key}: ${row.chars} (${row.result}${row.detail ? ': ' + row.detail.slice(0, 60) : ''})`);
    for (const lbl of new Set(rows.map(r => r.build)))
      for (const shape of new Set(rows.map(r => r.shape)))
        if (!(`${lbl} ${shape}` in lowest)) console.log(`#   ${lbl} ${shape}: every length drew`);
  })().catch(e => { console.error(e); process.exit(1); });
}

const mode = process.argv[2];
if (mode === '--scan') run(linkedShapes(process.argv[3]), { from: 300, to: 1975, step: 25 }, process.argv.slice(4));
else if (mode === '--scan-edges') run(edgeShapes(process.argv[3]), { from: 300, to: 1950, step: 25 }, process.argv.slice(4));
else if (mode === '--kinds') run(kindShapes(process.argv[3]), { from: 100, to: 1500, step: 50 }, process.argv.slice(4));
else {
  console.error('usage: node statement-limits-worker-probe.js --kinds|--scan|--scan-edges <dir> <shim page> <label>=<cdn base> [...]');
  console.error('The modeless form of 3.30.4 counted the engine\'s error pictures as drawn; --kinds replaces it.');
  process.exit(2);
}
