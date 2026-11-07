'use strict';
// Prints a jump.js result as: where the diagram read stood and stands, and how far the point that was at the window's
// middle (as a fraction of the diagram's height) now stands from the middle. usage: node summ-point.js <result.json> [vh]
const r = JSON.parse(require('fs').readFileSync(process.argv[2], 'utf8'));
const mid = (+process.argv[3] || 768) / 2;
const b = r.before, a = r.after;
const fy = (mid - b.top) / b.height;
const err = a.top + fy * a.h - mid;
console.log(JSON.stringify({ where: r.where, zoom: r.zoom, top: b.top + ' -> ' + a.top, h: b.height + ' -> ' + a.h,
  y: b.scrollY + ' -> ' + a.y, moved: a.movedBy, pointFraction: +fy.toFixed(3), pointOff: Math.round(err), onScreen: a.onScreen,
  errors: r.errors.length, parts: b.parts, landmark: r.landmark ? (r.landmark.words.slice(0, 24) + " " + r.landmark.y0 + " -> " + r.landmark.y1 + " (" + r.landmark.moved + ")") : null }));
if (r.proto) console.log("   proto:", r.proto.map((p) => p.t + " " + p.what + (p.dy !== undefined ? " " + p.dy : "") + (p.fy !== undefined ? " fy " + p.fy + " line " + p.line : "") + (p.el ? " " + p.el.slice(0, 30) : "") + (p.why ? " " + p.why : "")).join(" | "));
for (const w of r.writes) if (w.what !== 'scroll event') console.log('  ', w.t, w.what, '|', w.args, '| y', w.y, '|', String(w.stack || '').slice(0, 90));
