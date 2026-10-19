// The report's note controls on a drawn sequence diagram: how many notes the note script found (one
// rect.note-hover-rect each), whether each one sits on a note (its fill is a note colour, not a participant's), and
// whether hovering the first note shows its buttons. Run on today's page and on the look page: the counts must match.
// usage: node note-hover-check.js <page.local.html> [feature index=1]
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require('../../../tools/playwright/installed-browser.js');
const path = require('path');
(async () => {
  const [page, fi] = process.argv.slice(2);
  const b = await launchInstalled(chromium, { channel: 'chrome' });
  const ctx = await b.newContext({ viewport: { width: 1400, height: 1000 } });
  await ctx.addInitScript(() => { try { localStorage.setItem('kronikol.theme', 'light'); localStorage.removeItem('kronikol.reportSettings'); } catch (e) {} });
  const p = await ctx.newPage();
  const errors = []; p.on('pageerror', e => errors.push(e.message));
  await p.goto('file:///' + path.resolve(page).split(path.sep).join('/'));
  await p.waitForTimeout(1500);
  await p.evaluate(i => { const f = document.querySelectorAll('#report-content > details.feature')[i]; f.open = true; const s = f.querySelector('details.scenario'); s.open = true; s.querySelectorAll('details').forEach(d => d.open = true); s.scrollIntoView(); }, Number(fi || 1));
  for (let i = 0; i < 80; i++) { await p.waitForTimeout(250); if (await p.evaluate(() => { const svg = Array.from(document.querySelectorAll('#report-content .plantuml-browser svg')).find(x => x.getBoundingClientRect().width > 0); return !!(svg && svg.querySelector('.note-hover-rect')); })) break; }
  await p.waitForTimeout(800);
  const r = await p.evaluate(() => {
    const svg = Array.from(document.querySelectorAll('#report-content .plantuml-browser svg')).find(x => x.getBoundingClientRect().width > 0);
    if (!svg) return { error: 'no diagram' };
    const hovers = Array.from(svg.querySelectorAll('rect.note-hover-rect'));
    const shapes = Array.from(svg.querySelectorAll('path, rect, polygon')).filter(s => !s.classList.contains('note-hover-rect') && !s.closest('.note-toggle-icon')).map(s => ({ fill: (s.getAttribute('fill') || '').toLowerCase(), b: s.getBBox() })).filter(o => o.fill && o.fill !== 'none' && !/^#[0-9a-f]{6}00$/.test(o.fill) && o.b.width > 8);
    const onWhat = {};
    hovers.forEach(h => {
      const hb = h.getBBox(), cx = hb.x + hb.width / 2, cy = hb.y + hb.height / 2;
      const under = shapes.filter(o => o.b.x <= cx && cx <= o.b.x + o.b.width && o.b.y <= cy && cy <= o.b.y + o.b.height).sort((a, b2) => a.b.width * a.b.height - b2.b.width * b2.b.height)[0];
      const k = under ? under.fill : 'nothing';
      onWhat[k] = (onWhat[k] || 0) + 1;
    });
    const icons = svg.querySelectorAll('.note-toggle-icon').length;
    // hover the first note: are any of its buttons shown?
    let shownAfterHover = null;
    if (hovers[0]) {
      hovers[0].dispatchEvent(new MouseEvent('mouseenter', { bubbles: false }));
      shownAfterHover = Array.from(svg.querySelectorAll('.note-toggle-icon')).filter(g => { const cs = getComputedStyle(g); return cs.display !== 'none' && cs.visibility !== 'hidden' && cs.opacity !== '0'; }).length;
    }
    return { notesFound: hovers.length, hoverRectsOn: onWhat, buttons: icons, shownAfterHover, roles: svg.getAttribute('data-k-roles') };
  });
  console.log(path.basename(page), JSON.stringify(r), errors.length ? 'errors ' + JSON.stringify(errors.slice(0, 3)) : '');
  await b.close();
})();
