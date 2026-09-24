// Census of content clipped by a .feature/.scenario (content-visibility: auto = paint containment): the
// sweep's check 7, listing every culprit instead of the worst. Every <details> is opened and every holder
// laid out whole. A culprit is the outermost element past its holder's padding edge (left or right) that
// is not inside a scroll container or a nested holder below it. Also reports the page's own sideways
// scroll, measured before the holders are forced visible.
// Usage: node census.js <page.html,...> <w1,w2,...|from-to-step> [inject-css-file|-] [scrollbars=0|1]
const pw = require(require('path').resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const path = require('path');
const fs = require('fs');
const [, , filesArg, widthsArg, injectFile, sb] = process.argv;
const widths = widthsArg.includes('-')
  ? (() => { const [a, b, s] = widthsArg.split('-').map(Number); const w = []; for (let x = a; x <= b; x += s) w.push(x); return w; })()
  : widthsArg.split(',').map(Number);
const inject = injectFile && injectFile !== '-' ? fs.readFileSync(injectFile, 'utf8') : '';
(async () => {
  const browser = await pw[process.env.ENGINE || 'chromium'].launch(sb === '1' && !process.env.ENGINE ? { ignoreDefaultArgs: ['--hide-scrollbars'] } : {});
  for (const file of filesArg.split(',')) {
    const ctx = await browser.newContext({ viewport: { width: widths[0], height: 900 } });
    await ctx.route(/^https?:/, r => r.abort());
    const page = await ctx.newPage();
    const keys = new Map();
    const pageOver = [];
    for (const w of widths) {
      await page.setViewportSize({ width: w, height: 900 });
      await page.goto('file:///' + path.resolve(file).replace(/\\/g, '/'), { waitUntil: 'domcontentloaded', timeout: 120000 });
      if (inject) await page.addStyleTag({ content: inject });
      if (process.env.WRAP === '1') await page.evaluate(() => {
        // what the new emitter does: every grouped table sits in a .param-table-wrapper
        document.querySelectorAll('table.param-test-table').forEach(t => {
          if (t.parentElement.classList.contains('param-table-wrapper')) return;
          const w = document.createElement('div'); w.className = 'param-table-wrapper';
          t.parentElement.insertBefore(w, t); w.appendChild(t);
        });
      });
      if (process.env.SPACING === '1') await page.addStyleTag({ content: '*{line-height:1.5 !important;letter-spacing:0.12em !important;word-spacing:0.16em !important}p{margin-bottom:2em !important}' });
      await page.evaluate(() => {
        document.querySelectorAll('details').forEach(d => d.open = true);
        const f = document.querySelector('.filters'); if (f && getComputedStyle(f).display === 'none') f.style.display = 'flex';
        document.querySelectorAll('.scenario-diagram-controls').forEach(c => { c.style.display = 'flex'; });
      });
      await page.evaluate(() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))));
      const res = await page.evaluate(() => {
        const de = document.documentElement;
        const pageOver = de.scrollWidth - de.clientWidth;
        const name = el => el.localName + [...el.classList].filter(c => !/^(details-active|row-active|row-passed|row-failed|row-skipped|happy-path|passed|failed|skipped)$/.test(c)).map(c => '.' + c).join('');
        const scrolls = el => /^(auto|scroll|hidden)$/.test(getComputedStyle(el).overflowX);
        const vis = el => el.getBoundingClientRect().width > 0;
        const holders = [...document.querySelectorAll('.feature, .scenario')].filter(vis);
        holders.forEach(h => h.style.contentVisibility = 'visible');
        const found = [];
        const range = document.createRange();
        for (const h of holders) {
          const left = h.getBoundingClientRect().left + h.clientLeft, right = left + h.clientWidth;
          const past = r => Math.max(r.right - right, left - r.left);
          const walker = document.createTreeWalker(h, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT);
          for (let n = walker.nextNode(); n; n = walker.nextNode()) {
            const text = n.nodeType === Node.TEXT_NODE;
            if (text && !n.data.trim()) continue;
            const el = text ? n.parentElement : n;
            if (el.closest('svg') && (text || el.localName !== 'svg')) continue;
            let r;
            if (text) { range.selectNodeContents(n); r = range.getBoundingClientRect(); } else r = n.getBoundingClientRect();
            if (r.width === 0) continue;
            const over = past(r);
            if (over <= 1) continue;
            let elsewhere = false;
            for (let a = text ? el : el.parentElement; a && a !== h && !elsewhere; a = a.parentElement)
              elsewhere = a.matches('.feature, .scenario') || scrolls(a);
            if (elsewhere) continue;
            // the outermost culprit only: skip what sits inside an element already past the edge
            if (!text && el.parentElement !== h && past(el.parentElement.getBoundingClientRect()) > 1) continue;
            if (text && el !== h && past(el.getBoundingClientRect()) > 1) continue;
            const chain = [];
            for (let a = el; a && a !== h && chain.length < 4; a = a.parentElement) chain.unshift(name(a));
            found.push({ key: (h.classList.contains('scenario') ? 'S: ' : 'F: ') + chain.join(' > ') + (text ? ' #text' : ''), over: Math.round(over), text: (text ? n.data : el.textContent || '').trim().slice(0, 70) });
          }
        }
        return { found, pageOver };
      });
      if (res.pageOver > 1) pageOver.push(`${w}(+${res.pageOver})`);
      for (const f of res.found) {
        const k = keys.get(f.key) || { widths: new Set(), max: 0, count: 0, text: f.text };
        k.widths.add(w); k.count++;
        if (f.over >= k.max) { k.max = f.over; k.text = f.text; }
        keys.set(f.key, k);
      }
    }
    console.log(`== ${path.basename(file)}: page scrolls sideways at ${pageOver.join(' ') || 'no width'}`);
    const sorted = [...keys.entries()].sort((a, b) => b[1].max - a[1].max);
    for (const [key, k] of sorted.slice(0, 40)) {
      const ws = [...k.widths].sort((a, b) => a - b);
      console.log(`  +${k.max} px, ${ws[0]}-${ws[ws.length - 1]} (${ws.length} widths): ${key}`);
      console.log(`       e.g. ${JSON.stringify(k.text)}`);
    }
    await ctx.close();
  }
  await browser.close();
})();
