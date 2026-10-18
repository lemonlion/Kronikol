// Round 17 item 20 (owner's bug): clicking in and out of a diagram scrolls the page. Opens a scenario, shows its flame
// chart, places it mid-screen, then clicks with the real mouse (the flame chart, then the scenario's background) and
// logs scrollY before/after each click, the focused element, and every focus()/scrollIntoView()/scrollTo/scrollBy
// call with its stack, plus scroll events.
// usage: node r20-scroll.js <page> <chromium|firefox> <scenario text> [tab text, default "Flame"]
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const path = require('path');
(async () => {
  const [page, browserName, pick, tabText] = process.argv.slice(2);
  const b = await pw[browserName].launch(browserName === 'chromium' ? { channel: 'chrome' } : { executablePath: 'C:/Users/cex/AppData/Local/ms-playwright/firefox-1511/firefox/firefox.exe' });
  const ctx = await b.newContext(browserName === 'firefox' ? { viewport: null } : { viewport: { width: 1400, height: 900 } });
  await ctx.addInitScript(() => {
    try { localStorage.setItem('kronikol.theme', 'light'); localStorage.removeItem('kronikol.reportSettings'); } catch (e) {}
    window.__log = [];
    const tag = e => e && e.nodeType === 1 ? e.tagName.toLowerCase() + (e.id ? '#' + e.id : '') + (e.className && e.className.baseVal === undefined ? '.' + String(e.className).trim().split(/\s+/).slice(0, 2).join('.') : '') : String(e);
    const wrap = (obj, name) => { const f = obj[name]; obj[name] = function () { window.__log.push(name + ' on ' + tag(this) + ' y=' + Math.round(scrollY) + ' ' + (new Error().stack || '').split('\n').slice(1, 4).map(s => s.trim().slice(0, 90)).join(' < ')); return f.apply(this, arguments); }; };
    wrap(HTMLElement.prototype, 'focus'); wrap(SVGElement.prototype, 'focus'); wrap(Element.prototype, 'scrollIntoView');
    wrap(window, 'scrollTo'); wrap(window, 'scrollBy'); wrap(Element.prototype, 'scrollTo'); wrap(Element.prototype, 'scrollBy');
    addEventListener('focusin', e => window.__log.push('focusin ' + tag(e.target) + ' y=' + Math.round(scrollY)), true);
    let last = -1; addEventListener('scroll', () => { if (Math.round(scrollY) !== last) { last = Math.round(scrollY); window.__log.push('scroll y=' + last); } }, true);
  });
  const p = await ctx.newPage();
  await p.goto('file:///' + path.resolve(page).split(path.sep).join('/')); await p.waitForTimeout(3000);
  await p.evaluate(([pick, tabText]) => {
    const s = Array.from(document.querySelectorAll('details.scenario')).find(x => x.querySelector(':scope > summary').textContent.toLowerCase().includes(pick));
    s.closest('details.feature').open = true; s.open = true; s.setAttribute('data-probe', '1');
    const tab = Array.from(s.querySelectorAll('button, [role=tab], a')).find(t => t.textContent.trim().toLowerCase().startsWith((tabText || 'flame').toLowerCase()));
    if (tab) tab.click();
  }, [pick, tabText]);
  await p.waitForTimeout(2500);
  const target = await p.evaluate(() => {
    const s = document.querySelector('[data-probe]');
    const cands = Array.from(s.querySelectorAll('svg, canvas, .flame-chart, .flamechart, [class*=flame]')).filter(e => e.getBoundingClientRect().height > 40);
    const t = cands[0]; if (!t) return null;
    t.setAttribute('data-probe-target', '1');
    t.scrollIntoView({ block: 'center' });
    window.__log.length = 0;
    return t.tagName + '.' + String(t.className.baseVal !== undefined ? t.className.baseVal : t.className).slice(0, 40);
  });
  console.log('target', target);
  await p.waitForTimeout(800);
  const clickAt = async (label, fn) => {
    const pt = await p.evaluate(fn);
    const y0 = await p.evaluate(() => { window.__log.length = 0; return Math.round(scrollY); });
    await p.mouse.click(pt.x, pt.y);
    await p.waitForTimeout(900);
    const r = await p.evaluate(() => ({ y: Math.round(scrollY), active: (document.activeElement && document.activeElement.tagName) + '.' + String(document.activeElement && document.activeElement.className).slice(0, 50), log: window.__log.slice(0, 14) }));
    console.log('== ' + label + ' at ' + Math.round(pt.x) + ',' + Math.round(pt.y) + ': scrollY ' + y0 + ' -> ' + r.y + ' (moved ' + (r.y - y0) + ') active ' + r.active);
    r.log.forEach(l => console.log('   ' + l));
  };
  await clickAt('click diagram', () => { const r = document.querySelector('[data-probe-target]').getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + Math.min(r.height / 2, 60) }; });
  await clickAt('click scenario background', () => { const s = document.querySelector('[data-probe]'); const r = s.getBoundingClientRect(); const t = document.querySelector('[data-probe-target]').getBoundingClientRect(); return { x: r.left + 8, y: Math.max(t.top - 10, 120) }; });
  await clickAt('click diagram again', () => { const r = document.querySelector('[data-probe-target]').getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + Math.min(r.height / 2, 60) }; });
  await b.close();
})();
