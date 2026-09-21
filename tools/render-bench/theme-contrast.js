'use strict';
// For each themed SVG: report the text colour that lands inside Kronikol's fixed-colour
// note bodies (#d4edda assertion pass, #f8d7da assertion fail, #cfecf7 event) and the
// diagram background, so contrast breakage is measured rather than guessed.
const fs = require('fs'), path = require('path');
const pw = require('C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package');
const dir = path.join(__dirname, 'themeprobe', process.argv[2] || 'jar');
const svgs = fs.readdirSync(dir).filter(f => /\.svg$/.test(f) && f.length > 6).sort();

function lum(hex) {
  const h = hex.replace('#', '');
  if (h.length < 6) return null;
  const v = [0, 2, 4].map(i => {
    let c = parseInt(h.substr(i, 2), 16) / 255;
    return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
  });
  return 0.2126 * v[0] + 0.7152 * v[1] + 0.0722 * v[2];
}
function ratio(a, b) {
  const la = lum(a), lb = lum(b);
  if (la === null || lb === null) return null;
  return ((Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05)).toFixed(2);
}

(async () => {
  const browser = await pw.chromium.launch({ headless: true });
  const page = await browser.newPage();
  await page.setContent('<html><body><div id="h"></div></body></html>');
  const rows = [];
  for (const f of svgs) {
    const svg = fs.readFileSync(path.join(dir, f), 'utf8');
    const r = await page.evaluate((svgText) => {
      document.getElementById('h').innerHTML = svgText;
      const el = document.querySelector('#h svg');
      if (!el) return null;
      const style = el.getAttribute('style') || '';
      const bgm = style.match(/background:\s*([^;]+)/);
      const texts = Array.from(el.querySelectorAll('text'));
      function inside(fill) {
        const p = Array.from(el.querySelectorAll('path')).filter(x =>
          (x.getAttribute('fill') || '').toLowerCase() === fill);
        const out = [];
        p.forEach(pp => {
          const b = pp.getBBox();
          if (b.width < 10 || b.height < 8) return;
          texts.forEach(t => {
            const tb = t.getBBox();
            if (tb.x >= b.x - 2 && tb.x + tb.width <= b.x + b.width + 2
              && tb.y >= b.y - 2 && tb.y + tb.height <= b.y + b.height + 2) {
              out.push((t.getAttribute('fill') || '').toUpperCase());
            }
          });
        });
        return [...new Set(out)];
      }
      const bodyText = texts.map(t => (t.getAttribute('fill') || '').toUpperCase());
      const freq = {};
      bodyText.forEach(c => freq[c] = (freq[c] || 0) + 1);
      const dominant = Object.entries(freq).sort((a, b) => b[1] - a[1])[0];
      return {
        bg: bgm ? bgm[1].trim() : null,
        assertPass: inside('#d4edda'),
        assertFail: inside('#f8d7da'),
        event: inside('#cfecf7'),
        dominantText: dominant ? dominant[0] : null
      };
    }, svg);
    if (r) rows.push({ theme: f.replace(/\.svg$/, ''), ...r });
  }
  await browser.close();
  const PAGE = '#FFFFFF';   // the report page behind a background-less diagram
  console.log('theme                bg         domText   pass-note   event-note  contrast(text-on-page)');
  for (const r of rows) {
    const onPage = r.bg ? '-' : ratio(r.dominantText || '#000000', PAGE);
    console.log(
      r.theme.padEnd(20) +
      String(r.bg || '(none)').padEnd(11) +
      String(r.dominantText).padEnd(10) +
      JSON.stringify(r.assertPass).padEnd(12) +
      JSON.stringify(r.event).padEnd(12) +
      String(onPage)
    );
  }
})();
