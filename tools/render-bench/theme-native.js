'use strict';
// Reads each theme's OWN note styling out of a rendered SVG (no Kronikol pins applied),
// so a Kronikol note palette can be derived from the theme instead of overriding it.
// Writes theme-native.json.
const fs = require('fs'), path = require('path');
const pw = require('C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package');
const dir = path.join(__dirname, 'themeprobe', 'native');
const files = fs.readdirSync(dir).filter(f => f.endsWith('.svg')).sort();

(async () => {
  const browser = await pw.chromium.launch({ headless: true });
  const page = await browser.newPage();
  await page.setContent('<html><body><div id="h"></div></body></html>');
  const out = {};
  for (const f of files) {
    const svg = fs.readFileSync(path.join(dir, f), 'utf8');
    const r = await page.evaluate((s) => {
      document.getElementById('h').innerHTML = s;
      const el = document.querySelector('#h svg');
      const texts = Array.from(el.querySelectorAll('text'));
      const find = n => texts.find(t => t.textContent.indexOf(n) >= 0);
      const noteText = find('NoteBodyProbe');
      if (!noteText) return { err: 'probe text missing' };
      const tb = noteText.getBBox();
      // the smallest shape that fully contains the note text is the note body
      let body = null, bestArea = Infinity;
      Array.from(el.querySelectorAll('path,polygon,rect')).forEach(p => {
        const b = p.getBBox();
        if (b.width < 12 || b.height < 8) return;
        if (!(tb.x >= b.x - 4 && tb.x + tb.width <= b.x + b.width + 4
          && tb.y >= b.y - 4 && tb.y + tb.height <= b.y + b.height + 4)) return;
        const a = b.width * b.height;
        if (a < bestArea) { bestArea = a; body = p; }
      });
      const partText = find('ServiceBus');
      let partFill = null;
      if (partText) {
        const pb = partText.getBBox();
        let ba = Infinity;
        Array.from(el.querySelectorAll('path,polygon,rect')).forEach(p => {
          const b = p.getBBox();
          if (b.width < 12 || b.height < 8) return;
          if (!(pb.x >= b.x - 4 && pb.x + pb.width <= b.x + b.width + 4
            && pb.y >= b.y - 4 && pb.y + pb.height <= b.y + b.height + 4)) return;
          const a = b.width * b.height;
          if (a < ba) { ba = a; partFill = (p.getAttribute('fill') || '').toUpperCase(); }
        });
      }
      const freq = {};
      texts.forEach(t => { const c = (t.getAttribute('fill') || '').toUpperCase(); if (c) freq[c] = (freq[c] || 0) + 1; });
      const dom = Object.entries(freq).sort((a, b) => b[1] - a[1])[0];
      const link = find('LinkProbe');
      const arrow = (() => {
        const p = Array.from(el.querySelectorAll('path,line')).find(x => {
          const st = (x.getAttribute('stroke') || '');
          const b = x.getBBox();
          return st && b.width > 40 && b.height < 6;
        });
        return p ? (p.getAttribute('stroke') || '').toUpperCase() : null;
      })();
      return {
        noteFill: body ? (body.getAttribute('fill') || 'none').toUpperCase() : null,
        noteStroke: body ? (body.getAttribute('stroke') || 'none').toUpperCase() : null,
        noteText: (noteText.getAttribute('fill') || '').toUpperCase(),
        participantFill: partFill,
        participantText: partText ? (partText.getAttribute('fill') || '').toUpperCase() : null,
        domText: dom ? dom[0] : null,
        link: link ? (link.getAttribute('fill') || '').toUpperCase() : null,
        arrow
      };
    }, svg);
    out[f.replace(/\.svg$/, '')] = r;
  }
  await browser.close();
  fs.writeFileSync(path.join(__dirname, 'theme-native.json'), JSON.stringify(out, null, 1));
  console.log('theme                 noteFill    noteStroke  noteText   partFill    domText    link');
  Object.entries(out).forEach(([k, v]) => {
    if (v.err) { console.log(k.padEnd(21) + ' ERR ' + v.err); return; }
    console.log(k.padEnd(21) + String(v.noteFill).padEnd(12) + String(v.noteStroke).padEnd(12)
      + String(v.noteText).padEnd(11) + String(v.participantFill).padEnd(12)
      + String(v.domText).padEnd(11) + String(v.link));
  });
})();
