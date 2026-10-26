// Reads out/<engine>/results.json and prints the matrix.
const fs = require('fs');
const path = require('path');
const engines = ['chromium', 'firefox', 'webkit'];
const R = Object.fromEntries(engines.map(e => [e, JSON.parse(fs.readFileSync(path.join(__dirname, 'out', e, 'results.json'), 'utf8'))]));
const GROUPS = {
  'plain svg text': ['s0', 'm-json', 'm-part', 'm-step', 'm-link', 'o-zwsp'],
  'textLength spacing': ['s1', 's3', 's5', 's6'],
  'textLength spacingAndGlyphs': ['s2', 's4'],
  'across 3 <text>': ['m-cross'],
  'foreignObject html': ['o-box'],
  'html control': ['ctl'],
};
const ok = d => d && Math.abs(d.l) <= 3.5 && Math.abs(d.r) <= 3.5 && d.t <= 1.5 && d.t >= -12 && d.b >= -1.5 && d.b <= 9;
const worst = ds => { ds = ds.filter(Boolean); if (!ds.length) return '-'; const h = Math.max(...ds.map(d => Math.max(Math.abs(d.l), Math.abs(d.r)))); const v = Math.max(...ds.map(d => Math.max(Math.abs(d.t), Math.abs(d.b)))); return `h${h.toFixed(1)} v${v.toFixed(1)}`; };
console.log('== a: custom highlight API, which properties paint (pixels that turned the colour) ==');
for (const e of engines) {
  const a = R[e].a; if (!a) continue;
  for (const [g, ids] of Object.entries({ 'svg text': ['s0', 's1', 's2', 'm-json', 'm-step', 'm-link', 'm-cross', 'o-zwsp'], 'foreignObject html': ['o-box'], 'html control': ['ctl'] })) {
    const props = Object.keys(a[ids[0]].props);
    const paints = props.filter(p => ids.every(id => a[id].props[p].hue >= 8));
    const none = props.filter(p => ids.every(id => a[id].props[p].hue < 8));
    const mixed = props.filter(p => !paints.includes(p) && !none.includes(p));
    console.log(e.padEnd(9), g.padEnd(20), 'paints:', paints.join(',') || 'nothing', mixed.length ? ' mixed: ' + mixed.join(',') : '');
  }
}
console.log('\n== b/c: rect accuracy vs the ink (ok/total, worst horizontal and vertical error px) ==');
for (const [g, ids] of Object.entries(GROUPS)) {
  for (const e of engines) {
    const b = R[e].b; const line = [];
    for (const tech of ['R', 'X', 'S']) {
      const rows = [];
      for (const st of Object.keys(b)) for (const id of ids) { const row = b[st][id]; if (row && !row.offscreen && row[tech] !== undefined) rows.push({ st, id, d: row[tech] }); }
      const valid = rows.filter(r => r.d);
      if (!valid.length) continue;
      const nOk = valid.filter(r => ok(r.d)).length;
      const bad = valid.filter(r => !ok(r.d)).map(r => r.st + ':' + r.id);
      line.push(`${tech} ${nOk}/${valid.length} ${worst(valid.map(r => r.d))}${bad.length && bad.length <= 6 ? ' bad[' + bad.join(' ') + ']' : bad.length ? ' bad[' + bad.length + ']' : ''}`);
    }
    console.log(g.padEnd(28), e.padEnd(9), line.join(' | '));
  }
}
console.log('\n== tspan split moved the ink? (Z0, whole text red via tspans vs via style) ==');
for (const e of engines) {
  const moved = Object.entries(R[e].b.Z0).filter(([id, r]) => r.tspanCheck && (Math.abs(r.tspanCheck.l) > 0.5 || Math.abs(r.tspanCheck.r) > 0.5)).map(([id, r]) => `${id}${JSON.stringify(r.tspanCheck)}`);
  console.log(e.padEnd(9), moved.length ? moved.join(' ') : 'none');
}
console.log('\n== d: deep match in the box ==');
for (const e of engines) console.log(e.padEnd(9), Object.entries(R[e].d).map(([k, v]) => `${k}:${v.inBox && v.inWindow ? 'ok' : 'MISS'}${v.hl ? '(hl ' + v.hl.n + ')' : ''}`).join(' '));
console.log('\n== e: overlay centre offset from the ink after each change (px) ==');
for (const e of engines) for (const [id, rows] of Object.entries(R[e].e)) {
  console.log(e.padEnd(9), id.padEnd(7), Object.entries(rows).map(([st, v]) => `${st}: svg ${v.svgRect ? Math.max(Math.abs(v.svgRect.dx), Math.abs(v.svgRect.dy)).toFixed(0) : '?'} html ${v.html ? Math.max(Math.abs(v.html.dx), Math.abs(v.html.dy)).toFixed(0) : '?'}`).join(' | '));
}
console.log('\n== f: timing (ms) ==');
for (const e of engines) { const f = R[e].f; for (const k of ['fifty', 'many']) { const v = f[k]; console.log(e.padEnd(9), String(v.matches).padStart(3), 'matches: search', v.search, 'layout', v.layoutOnly, '| Range rects warm/cold', v.R_warm, v.R_cold, '| extentOfChar', v.X_warm, v.X_cold, '| substring', v.S_warm, v.S_cold, '| rects per text', v.insertRects, '| one multiply layer', v.insertLayer, '| highlight 2 frames', v.hlTwoFrames, 'idle', v.idleTwoFrames); } console.log(e.padEnd(9), ' box', JSON.stringify(f.box)); }
