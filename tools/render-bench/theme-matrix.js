'use strict';
// Full acceptance matrix for a themed Kronikol diagram rendered by the PRODUCTION browser engine.
// For each themed SVG: runs the report's real detectors, and extracts every colour Kronikol's
// report code or a reader depends on.
//   usage: node theme-matrix.js [subdir]   (default js43)
const fs = require('fs'), path = require('path');
const pw = require('C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package');
const SRC = 'C:/Code/Kronikol/src/Kronikol/Reports/collapsible-notes-script.js';
const lines = fs.readFileSync(SRC, 'utf8').split('\n');
const fns = lines.slice(33, 186).join('\n') + '\n' + lines.slice(1778, 1828).join('\n') + '\n'
  + 'window.__findNoteGroups=findNoteGroups;window.__findAssert=findAssertionNoteGroups;';
const dir = path.join(__dirname, 'themeprobe', process.argv[2] || 'js43');
const files = fs.readdirSync(dir).filter(f => f.endsWith('.svg')).sort();

function lum(h) {
  h = String(h).replace('#', '');
  if (!/^[0-9a-fA-F]{6}$/.test(h)) return null;
  const v = [0, 2, 4].map(i => {
    const c = parseInt(h.substr(i, 2), 16) / 255;
    return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
  });
  return 0.2126 * v[0] + 0.7152 * v[1] + 0.0722 * v[2];
}
function ratio(a, b) {
  const la = lum(a), lb = lum(b);
  if (la === null || lb === null) return null;
  return +((Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05)).toFixed(2);
}

(async () => {
  const browser = await pw.chromium.launch({ headless: true });
  const page = await browser.newPage();
  await page.setContent('<html><body><div id="h"></div></body></html>');
  await page.evaluate((src) => { (0, eval)(src); }, fns);
  const rows = [];
  for (const f of files) {
    const svg = fs.readFileSync(path.join(dir, f), 'utf8');
    const r = await page.evaluate((s) => {
      document.getElementById('h').innerHTML = s;
      const el = document.querySelector('#h svg');
      if (!el) return { fatal: 'no svg' };
      const texts = Array.from(el.querySelectorAll('text'));
      const banner = texts.filter(t => /Please use CSS style/.test(t.textContent)).length;
      const groups = window.__findNoteGroups(el);
      const asserts = window.__findAssert(el);

      // text colour by content, so each Kronikol construct is located by what it says
      const byText = (needle) => {
        const t = texts.find(x => x.textContent.indexOf(needle) >= 0);
        return t ? (t.getAttribute('fill') || '').toUpperCase() : null;
      };
      // the fill of the shape a given text sits inside
      const fillUnder = (needle) => {
        const t = texts.find(x => x.textContent.indexOf(needle) >= 0);
        if (!t) return null;
        const tb = t.getBBox();
        let best = null, bestArea = Infinity;
        Array.from(el.querySelectorAll('path,polygon,rect')).forEach(p => {
          const fill = (p.getAttribute('fill') || '');
          if (!fill || fill === 'none' || /^#[0-9a-f]{6}00$/i.test(fill)) return;
          const b = p.getBBox();
          if (b.width < 12 || b.height < 8) return;
          if (!(tb.x >= b.x - 3 && tb.x + tb.width <= b.x + b.width + 3
            && tb.y >= b.y - 3 && tb.y + tb.height <= b.y + b.height + 3)) return;
          const a = b.width * b.height;
          if (a < bestArea) { bestArea = a; best = fill.toUpperCase(); }
        });
        return best;
      };
      // dominant body-text colour (arrow labels / participant names)
      const freq = {};
      texts.forEach(t => {
        const c = (t.getAttribute('fill') || '').toUpperCase();
        if (c) freq[c] = (freq[c] || 0) + 1;
      });
      const dom = Object.entries(freq).sort((a, b) => b[1] - a[1])[0];

      const link = texts.find(t => t.textContent.indexOf('LinkProbe') >= 0);
      const lifeline = (() => {
        const r = Array.from(el.querySelectorAll('rect')).find(x =>
          parseFloat(x.getAttribute('width') || 0) < 12 && parseFloat(x.getAttribute('height') || 0) > 80);
        return r ? (r.getAttribute('stroke') || r.getAttribute('fill') || '').toUpperCase() : null;
      })();

      return {
        banner,
        notes: groups.length,
        asserts: asserts.length,
        domText: dom ? dom[0] : null,
        participantFill: fillUnder('ServiceBus'),
        participantText: byText('ServiceBus'),
        noteFill: fillUnder('NoteBodyProbe'),
        noteText: byText('NoteBodyProbe'),
        headerText: byText('HeaderProbe'),
        eventFill: fillUnder('EventBodyProbe'),
        eventText: byText('EventBodyProbe'),
        assertFill: fillUnder('AssertProbe'),
        assertText: byText('AssertProbe'),
        stepFill: fillUnder('StepBarProbe'),
        stepText: byText('StepBarProbe'),
        linkText: link ? (link.getAttribute('fill') || '').toUpperCase() : null,
        arrowLabel: byText('GetBreakfast'),
        lifeline
      };
    }, svg);
    rows.push({ theme: f.replace(/\.svg$/, ''), ...r });
  }
  await browser.close();

  const bad = [];
  console.log('theme                 bn note asrt | noteFill/txt      hdr/note  event/txt  assert/txt  step/txt   link      domText');
  for (const r of rows) {
    if (r.fatal) { console.log(r.theme.padEnd(21) + ' FATAL ' + r.fatal); bad.push(r.theme + ': no svg'); continue; }
    const c = {
      note: ratio(r.noteText, r.noteFill),
      hdr: ratio(r.headerText, r.noteFill),
      event: ratio(r.eventText, r.eventFill),
      assert: ratio(r.assertText, r.assertFill),
      step: ratio(r.stepText, r.stepFill)
    };
    if (r.banner) bad.push(r.theme + ': deprecation banner');
    if (r.notes !== 3) bad.push(`${r.theme}: notes ${r.notes}/3`);
    if (r.asserts !== 1) bad.push(`${r.theme}: asserts ${r.asserts}/1`);
    Object.entries(c).forEach(([k, v]) => { const min = (k === 'hdr') ? 3.0 : 4.5; if (v !== null && v < min) bad.push(`${r.theme}: ${k} contrast ${v}`); });
    console.log(
      r.theme.padEnd(21) +
      String(r.banner).padStart(2) + ' ' +
      String(r.notes).padStart(4) + ' ' + String(r.asserts).padStart(4) + ' | ' +
      `${r.noteFill}/${r.noteText}`.padEnd(18) +
      String(c.hdr).padEnd(9) + String(c.event).padEnd(10) + String(c.assert).padEnd(11) +
      String(c.step).padEnd(10) + String(r.linkText).padEnd(9) + String(r.domText)
    );
  }
  console.log('\n--- issues (' + bad.length + ') ---');
  bad.forEach(b => console.log('  ' + b));
  fs.writeFileSync(path.join(__dirname, 'theme-matrix.json'), JSON.stringify(rows, null, 1));
})();
