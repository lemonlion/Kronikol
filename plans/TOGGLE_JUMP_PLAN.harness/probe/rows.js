'use strict';
// Runs the plan's rows through jump.js one after another and prints one line each: how far the diagram read moved,
// how far the text that stood nearest the window's middle moved (the reader's content: 0 is a kept place), and whether
// the diagram read is still on screen. The environment is passed on (BROWSER, PROTO, PROTO_OFF).
// usage: node rows.js [row ...]   (no rows: all of them)
const { execFileSync } = require('child_process');
const path = require('path');
const ROWS = {
  M1a: { args: ['wide-long-note', 'headers', 'in', 'mid', '0'] },
  M1b: { args: ['wide-long-note', 'headers', 'max', 'mid', '0'] },
  M1c: { args: ['wide-long-note', 'headers', 'in', 'top', '0'] },
  M3top: { args: ['wide-many', 'headers', 'in', 'top', '3'] },
  M3topNone: { args: ['wide-many', 'headers', 'none', 'top', '3'] },
  M3cut: { args: ['wide-many', 'headers', 'none', 'cut', '3'] },
  M3gap: { args: ['wide-many', 'headers', 'none', 'gap', '3'] },
  M3gapIn: { args: ['wide-many', 'headers', 'in', 'gap', '3'] },
  M3mid: { args: ['wide-many', 'headers', 'in', 'mid', '3'] },
  M3deep: { args: ['wide-many', 'headers', 'in', 'deep:1117', '3'] },
  M4: { args: ['wide-many', 'headers', 'none', 'gap', '3'], env: { ZOOM_ALSO: '0,1,2' } },
  M4mid: { args: ['wide-many', 'headers', 'in', 'mid', '3'], env: { ZOOM_ALSO: '0,1,2' } },
  M5: { args: ['dense', 'steps', 'in', 'mid', '8'] },
  T7: { args: ['wide-in-parts', 'headers', 'in', 'deep:2000', '0'] },
  T9: { args: ['wide-many', 'headers', 'in', 'mid', '3'], env: { SCOPE: 'scenario' } },
  T9top: { args: ['wide-many', 'headers', 'in', 'top', '3'], env: { SCOPE: 'scenario' } },
};
const pick = process.argv.slice(2);
const fixtures = path.join(__dirname, '..', 'fixtures');
for (const name of pick.length ? pick : Object.keys(ROWS)) {
  const row = ROWS[name];
  if (!row) { console.log(name, 'unknown row'); continue; }
  const args = [path.join(__dirname, 'jump.js'), path.join(fixtures, row.args[0]), ...row.args.slice(1)];
  let r;
  try {
    r = JSON.parse(execFileSync(process.execPath, args, { env: Object.assign({}, process.env, row.env || {}), maxBuffer: 1 << 26, timeout: 300000 }).toString());
  } catch (e) { console.log(name.padEnd(10), 'FAILED', String(e.stdout || e.message).slice(0, 300)); continue; }
  const lm = r.landmark;
  const keeps = (r.writes || []).filter((w) => /^scrollBy/.test(w.what) && /keepPlace/.test(w.stack || '')).length;
  const proto = (r.proto || []).map((p) => p.what + (p.dy !== undefined ? ' ' + p.dy : '') + (p.why ? ' ' + p.why : '')).join(', ');
  console.log(name.padEnd(10), 'moved', String(r.after.movedBy).padStart(6), ' text', String(lm ? (lm.lost ? 'lost' : lm.moved) : '-').padStart(6),
    ' near', String(lm && lm.near ? lm.near.median + ' [' + lm.near.min + ',' + lm.near.max + ']' : '-').padStart(14), ' on screen', r.after.onScreen ? 'yes' : 'NO ', ' keepPlace', keeps, r.errors.length ? ' ERRORS ' + r.errors.join('; ').slice(0, 200) : '', proto ? ' | ' + proto.slice(0, 160) : '');
}
