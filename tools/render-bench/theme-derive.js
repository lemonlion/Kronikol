'use strict';
// Derives a full Kronikol note palette FROM each theme's own measured styling
// (theme-native.json), instead of overriding it with fixed colours.
//
// Rules:
//   noteBase   = the theme's own note fill; transparent/none -> the theme's page background
//                (keeps the outline look while giving hasNoteFill() something to find)
//   noteText   = the theme's own note text, auto-corrected only if it fails 4.5 on noteBase
//   event/pass/fail = noteBase hue-rotated at constant lightness, so the semantic tints
//                belong to the theme and inherit its text contrast
//   header     = noteText blended toward noteBase until it lands just past 3.0
//   step bar   = the theme's own participant fill/text pair, or inverse video as a fallback
//   partition  = the page background nudged toward noteText
// Writes themeprobe/derived/*.puml and theme-palettes.json.
const fs = require('fs'), path = require('path'), zlib = require('zlib');
const native = require('./theme-native.json');

// ---------- colour maths ----------
const hex = c => '#' + c.map(v => Math.round(Math.max(0, Math.min(255, v))).toString(16).padStart(2, '0')).join('').toUpperCase();
function parse(h) {
  if (!h) return null;
  h = h.replace('#', '');
  if (h.length === 8) { if (h.slice(6).toLowerCase() === '00') return null; h = h.slice(0, 6); }
  if (!/^[0-9a-fA-F]{6}$/.test(h)) return null;
  return [0, 2, 4].map(i => parseInt(h.substr(i, 2), 16));
}
function lum(c) {
  const v = c.map(x => { x /= 255; return x <= 0.03928 ? x / 12.92 : Math.pow((x + 0.055) / 1.055, 2.4); });
  return 0.2126 * v[0] + 0.7152 * v[1] + 0.0722 * v[2];
}
const ratio = (a, b) => {
  const la = lum(a), lb = lum(b);
  return +((Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05)).toFixed(2);
};
function toHsl([r, g, b]) {
  r /= 255; g /= 255; b /= 255;
  const mx = Math.max(r, g, b), mn = Math.min(r, g, b), d = mx - mn;
  let h = 0;
  if (d) {
    if (mx === r) h = ((g - b) / d) % 6;
    else if (mx === g) h = (b - r) / d + 2;
    else h = (r - g) / d + 4;
    h *= 60; if (h < 0) h += 360;
  }
  const l = (mx + mn) / 2;
  const s = d === 0 ? 0 : d / (1 - Math.abs(2 * l - 1));
  return [h, s, l];
}
function fromHsl([h, s, l]) {
  const c = (1 - Math.abs(2 * l - 1)) * s, x = c * (1 - Math.abs((h / 60) % 2 - 1)), m = l - c / 2;
  let r, g, b;
  if (h < 60) [r, g, b] = [c, x, 0]; else if (h < 120) [r, g, b] = [x, c, 0];
  else if (h < 180) [r, g, b] = [0, c, x]; else if (h < 240) [r, g, b] = [0, x, c];
  else if (h < 300) [r, g, b] = [x, 0, c]; else [r, g, b] = [c, 0, x];
  return [(r + m) * 255, (g + m) * 255, (b + m) * 255];
}
const mix = (a, b, t) => a.map((v, i) => v + (b[i] - v) * t);

// CIE76 distance, used to keep the semantic tints readable AS CATEGORIES, not just legible.
// The shipped default palette (#FEFFDD note vs #CFECF7 / #D4EDDA / #F8D7DA) has a minimum
// pairwise distance of 14, so that is the floor a derived palette has to clear.
const MIN_DE = 14;
function lab(c) {
  const [r, g, b] = c.map(v => { v /= 255; return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); });
  const x = (r * 0.4124 + g * 0.3576 + b * 0.1805) / 0.95047;
  const y = r * 0.2126 + g * 0.7152 + b * 0.0722;
  const z = (r * 0.0193 + g * 0.1192 + b * 0.9505) / 1.08883;
  const f = t => t > 0.008856 ? Math.cbrt(t) : (7.787 * t + 16 / 116);
  return [116 * f(y) - 16, 500 * (f(x) - f(y)), 200 * (f(y) - f(z))];
}
const dE = (a, b) => { const A = lab(a), B = lab(b); return Math.hypot(A[0] - B[0], A[1] - B[1], A[2] - B[2]); };

// Hue-rotate at (near-)constant lightness. A near-white base is pulled down slightly so the
// tint is visible; saturation gets a floor so achromatic themes still produce a readable tint.
function tintAt(base, hue, l, sBoost) {
  const s = toHsl(base)[1];
  const sT = l > 0.85 ? 0.55 : Math.max(s, 0.32);
  return fromHsl([hue, Math.min(Math.max(sT, sBoost), 0.78), l]);
}
// Hue-rotate at constant lightness first. When the theme's own note colour already sits at
// that hue (a cyan note vs the cyan event tint), the rotation changes nothing, so walk the
// lightness away from the base until the tint is a distinct CATEGORY -- while never letting
// the note's text contrast drop below 4.5.
function tint(base, hue, text) {
  let l0 = toHsl(base)[2];
  if (l0 > 0.95) l0 = 0.92;
  if (l0 < 0.06) l0 = 0.10;
  const flat = tintAt(base, hue, l0, 0);
  if (dE(base, flat) >= MIN_DE && (!text || ratio(text, flat) >= 4.5)) return flat;
  const dirs = l0 > 0.5 ? [-1, 1] : [1, -1];
  let fallback = flat;
  for (let d = 0.05; d <= 0.5; d += 0.03) {
    for (const dir of dirs) {
      const l = l0 + dir * d;
      if (l < 0.10 || l > 0.94) continue;
      const cand = tintAt(base, hue, l, 0.42);
      if (text && ratio(text, cand) < 4.5) continue;
      if (dE(base, cand) >= MIN_DE) return cand;
      fallback = cand;
    }
  }
  return fallback;
}

// ---------- derivation ----------
const HUE = { event: 196, pass: 132, fail: 4 };
// backgrounds the themes declare for themselves; everything else falls to a neutral by family
const DECLARED = {
  blueprint: '#003153', 'carbon-gray': '#000000', 'crt-amber': '#282828', 'crt-green': '#282828',
  mars: '#F9F9F9', mimeograph: '#D9D3D0', plain: '#FFFFFF', 'spacelab-white': '#FFFFFF',
  toy: '#DDDDDD', vibrant: '#FFFFFF', amiga: '#0B58A8', sunlust: '#FDF6E3',
  'reddress-darkblue': '#777777', 'reddress-darkgreen': '#777777',
  'reddress-darkorange': '#777777', 'reddress-darkred': '#777777'
};
// hand-picked where a derived neutral leaves the theme's own body text under 4.5
const BG_OVERRIDE = {
  'cerulean-outline': '#FFFFFF', 'cyborg-outline': '#1B1B1B', 'materia-outline': '#FFFFFF',
  'superhero-outline': '#2B3E50', 'spacelab-white': '#F2F5F9', mimeograph: '#F2EFEC'
};

function background(name, n) {
  if (BG_OVERRIDE[name]) return parse(BG_OVERRIDE[name]);
  const d = DECLARED[name];
  if (d) { const p = parse(d); return (p && lum(p) < 0.02) ? parse('#141414') : p; }
  const dom = parse(n.domText) || [0, 0, 0];
  return lum(dom) > 0.35 ? parse('#1E1E1E') : parse('#FFFFFF');
}

function derive(name, n) {
  const bg = background(name, n);
  let base = parse(n.noteFill) || bg.slice();       // transparent note -> page background
  if (lum(base) < 0.005) base = parse('#0A0A0A');   // never pure black: hasNoteFill() rejects it
  let text = parse(n.noteText) || [0, 0, 0];
  if (ratio(text, base) < 4.5) {
    // keep the theme's hue, but make the pair legible: try its own ink first, then the
    // better of near-black / near-white, then walk the base's lightness away from the text
    const dark = [16, 16, 16], light = [236, 236, 236];
    text = ratio(dark, base) >= ratio(light, base) ? dark : light;
    if (ratio(text, base) < 4.5) {
      const [h, s] = toHsl(base);
      const up = lum(text) < 0.5;            // dark ink -> lighten the base, and vice versa
      for (let step = 1; step <= 20; step++) {
        const l = Math.max(0.04, Math.min(0.97, toHsl(base)[2] + (up ? 1 : -1) * step * 0.04));
        const cand = fromHsl([h, s, l]);
        if (ratio(text, cand) >= 4.5) { base = cand; break; }
        base = cand;
      }
    }
  }
  const border = parse(n.noteStroke) || mix(base, text, 0.35);

  // header: pull the note text toward the note fill, stopping at today's shipped dimming level
  let header = text;
  for (let t = 0.05; t <= 0.85; t += 0.05) {
    const c = mix(text, base, t);
    if (ratio(c, base) < 3.9) break;   // 3.87 is today's #808080-on-#FEFFDD; don't regress it
    header = c;
  }

  // step bar: the theme's own participant pair when it is opaque and legible, else inverse video
  let barBg = parse(n.participantFill), barFg = parse(n.participantText);
  if (!barBg || !barFg || ratio(barBg, barFg) < 4.5) { barBg = parse(n.domText) || text; barFg = bg.slice(); }
  if (ratio(barBg, barFg) < 4.5) { barBg = lum(bg) > 0.4 ? [40, 40, 40] : [232, 232, 232]; barFg = lum(bg) > 0.4 ? [245, 245, 245] : [26, 26, 26]; }

  const ev = tint(base, HUE.event, text);
  const pass = tint(base, HUE.pass, text);
  let fail = tint(base, HUE.fail, text);
  if (dE(pass, fail) < MIN_DE) {
    // hue-opposed already; if they still collide the base is near-achromatic and very dark,
    // so separate them on lightness too
    const l = Math.min(0.94, Math.max(0.10, toHsl(fail)[2] + (toHsl(pass)[2] > 0.5 ? -0.14 : 0.14)));
    const cand = tintAt(base, HUE.fail, l, 0.45);
    if (ratio(text, cand) >= 4.5) fail = cand;
  }

  return {
    background: hex(bg),
    noteBackground: hex(base),
    noteBorder: hex(border),
    noteText: hex(text),
    headerText: hex(header),
    eventNote: hex(ev),
    assertPass: hex(pass),
    assertFail: hex(fail),
    stepBar: hex(barBg),
    stepBarText: hex(barFg),
    setupPartition: hex(mix(bg, text, 0.07)),
    link: n.link || '#0000FF'
  };
}

// ---------- emit probes ----------
const BODY = p => `
!pragma teoz true
skinparam noteBackgroundColor ${p.noteBackground}
skinparam noteBorderColor ${p.noteBorder}
skinparam noteFontColor ${p.noteText}
skinparam hyperlinkColor ${p.link}
<style>
 .eventNote { BackgroundColor ${p.eventNote}
     FontColor ${p.noteText}
     FontSize 11
     RoundCorner 10
 }
 .assertionNote { BackgroundColor ${p.assertPass}
     FontColor ${p.noteText}
     FontSize 11
     RoundCorner 5
 }
</style>
skinparam wrapWidth 800
autonumber 1

actor "Caller" as caller
entity "Breakfast Provider" as breakfastProvider
database "CosmosDB" as cosmosDB
queue "ServiceBus" as bus

hnote across ${p.stepBar}:<color:${p.stepBarText}>StepBarProbe
caller -[#438DD5]> breakfastProvider: GetBreakfast
note right
<color:${p.headerText}>HeaderProbe
NoteBodyProbe
end note
breakfastProvider -[#E74C3C]> cosmosDB: query
note right
NoteBodyTwo
end note
breakfastProvider -[#9B59B6]> bus: publish
note<<eventNote>> right
EventBodyProbe
end note
hnote across <<assertionNote>>
✓ AssertProbe
end note
breakfastProvider --> caller: [[https://example.com/x LinkProbe]]
@enduml
`;

const themeDir = path.join(__dirname, 'themeprobe', 'themetext');
const outDir = path.join(__dirname, 'themeprobe', 'derived');
fs.mkdirSync(outDir, { recursive: true });
const palettes = {};
for (const [name, n] of Object.entries(native)) {
  if (n.err) { console.log('SKIP', name, n.err); continue; }
  const p = derive(name, n);
  palettes[name] = p;
  const themeFile = path.join(themeDir, name + '.txt');
  const themeText = name === '_baseline' ? '' : fs.readFileSync(themeFile, 'utf8');
  fs.writeFileSync(path.join(outDir, name + '.puml'), '@startuml\n' + themeText + '\n' + BODY(p));
}
fs.writeFileSync(path.join(__dirname, 'theme-palettes.json'), JSON.stringify(palettes, null, 1));

console.log('theme                 bg       note     text     hdr      event    pass     fail     bar/txt');
for (const [k, p] of Object.entries(palettes)) {
  console.log(k.padEnd(21) + [p.background, p.noteBackground, p.noteText, p.headerText,
    p.eventNote, p.assertPass, p.assertFail].map(x => x.padEnd(9)).join('')
    + p.stepBar + '/' + p.stepBarText);
}
console.log('\nwrote', Object.keys(palettes).length, 'palettes +', Object.keys(palettes).length, 'probes');
