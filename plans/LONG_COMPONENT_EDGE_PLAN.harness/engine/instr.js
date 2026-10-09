'use strict';
// node --stack-size=60000 instr.js <UNOBFUSCATED engine.js> <shape> <len> [<len> ...]
// Instruments TeaVM's java.util.regex port: every virtual `matches` call of a regex node (jur_*_matches, dispatched
// through $rt_wrapFunction3) counts one level of regex nesting. Each Matcher entry (lookingAt / find0) starts a new
// measurement tagged with the pattern and the input length; the caller (first non-regex Java frames) is taken from
// a stack sample the first time a measurement passes DEPTH_SAMPLE levels. Prints, per render, every regex call whose
// nesting reached MIN_REPORT, with its max depth, the input length and its caller.
const h = require('./harness'), g = require('./gen');
const [engine, shape, ...lens] = process.argv.slice(2);
const VIZ = process.env.VIZ || 'C:/Users/cex/AppData/Local/Kronikol/plantuml-js/1.2026.8/viz-global.js';
const out = (s) => process.stdout.write(s + String.fromCharCode(10));
const MIN_REPORT = Number(process.env.MIN_REPORT || 60), DEPTH_SAMPLE = Number(process.env.DEPTH_SAMPLE || 50);

const R = globalThis.__rx = {
  d: 0, cur: null, recs: [],
  str(x) { try { if (x == null) return ''; if (typeof x === 'string') return x; if (x.$nativeString != null) return String(x.$nativeString); if (x.toString) return String(x.toString()); } catch (_) {} return '?'; },
  begin(m) {
    this.flush();
    let pat = '?', inLen = -1;
    try { pat = this.str(m.$pat.$lexemes.$orig); } catch (_) {}
    try { const s = m.$string2; inLen = s.$nativeString != null ? s.$nativeString.length : s.$length(); } catch (_) {}
    this.cur = { pat, inLen, max: 0, caller: null, d0: this.d };
  },
  flush() { if (this.cur && this.cur.max >= MIN_REPORT) this.recs.push(this.cur); this.cur = null; },
  hit() {
    const c = this.cur; if (!c) return;
    const depth = this.d - c.d0;
    if (depth > c.max) {
      c.max = depth;
      if (depth === DEPTH_SAMPLE && !c.caller) {
        const old = Error.stackTraceLimit; Error.stackTraceLimit = 400;
        const st = new Error().stack.split('\n').slice(1).map(l => l.trim().replace(/^at /, '').replace(/ \(.*$/, ''));
        Error.stackTraceLimit = old;
        c.caller = st.filter(n => !/^jur_|__rx|^Object\.|^\$rt_|wrapFunction|^function|^<anonymous>|^jl_String|^jl_CharSequence/.test(n)).slice(0, 4).join(' < ');
      }
    }
  }
};

function transform(code) {
  let n = 0;
  code = code.replace('$rt_wrapFunction3 = f => function(p1, p2, p3) {\n    return f(this, p1, p2, p3);\n},', () => { n++; return '$rt_wrapFunction3 = f => (/^jur_\\w*_matches\\d*$/.test(f.name) ? function(p1, p2, p3) { const R = globalThis.__rx; R.d++; R.hit(); try { return f(this, p1, p2, p3); } finally { R.d--; } } : function(p1, p2, p3) {\n    return f(this, p1, p2, p3);\n}),'; });
  code = code.replace('jur_Matcher_lookingAt = ($this, $startIndex, $mode) => {', (m) => { n++; return m + ' globalThis.__rx.begin($this);'; });
  code = code.replace('jur_Matcher_find0 = ($this, $start) => {', (m) => { n++; return m + ' globalThis.__rx.begin($this);'; });
  if (n !== 3) throw new Error('instrumentation patched ' + n + ' of 3 sites (needs an unobfuscated build)');
  return code;
}

(async () => {
  await h.init(engine, VIZ, transform);
  for (const l of lens) {
    R.recs = []; R.cur = null;
    const { statement, source } = g.shapeSource(shape, Number(l));
    const r = await h.render(source);
    R.flush();
    const recs = R.recs.slice().sort((a, b) => b.max - a.max);
    out(`== ${shape} stmt=${statement.length} verdict=${r.verdict} regex calls nesting >= ${MIN_REPORT}: ${recs.length}`);
    const seen = new Map();
    for (const x of recs) { const k = x.pat + '|' + x.caller; const v = seen.get(k); if (!v) seen.set(k, { ...x, n: 1 }); else v.n++; }
    for (const x of [...seen.values()].slice(0, Number(process.env.TOP || 12)))
      out(`  max=${String(x.max).padStart(5)} input=${String(x.inLen).padStart(5)} x${x.n}  ${x.caller}\n      /${x.pat.length > 160 ? x.pat.slice(0, 160) + '…' : x.pat}/`);
  }
  process.exit(0);
})().catch(e => { console.error(e && e.stack || e); process.exit(1); });
