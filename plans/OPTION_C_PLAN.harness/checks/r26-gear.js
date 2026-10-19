// Item 26: each scenario's settings bar opens from a gear at the top right of its diagram, the gear stays put when the
// bar opens, the open bar sticks under the report's bar while its diagram is in view, and nothing is report-wide.
// usage: node r26-gear.js <page> <chromium|firefox> [shots prefix]
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require('../../../tools/playwright/installed-browser.js');
const path = require('path');
(async () => {
  const [page, browserName, shots] = process.argv.slice(2);
  const b = await launchInstalled(pw[browserName], browserName === 'chromium' ? { channel: 'chrome' } : { executablePath: 'C:/Users/cex/AppData/Local/ms-playwright/firefox-1511/firefox/firefox.exe', args: ['-width', '1416', '-height', '1000'] });
  const ctx = await b.newContext(browserName === 'firefox' ? { viewport: null } : { viewport: { width: 1400, height: 900 } });
  await ctx.addInitScript(() => { try { localStorage.setItem('kronikol.theme', 'light'); localStorage.removeItem('kronikol.reportSettings'); } catch (e) {} });
  const p = await ctx.newPage();
  const errors = []; p.on('pageerror', e => errors.push(String(e)));
  await p.goto('file:///' + path.resolve(page).split(path.sep).join('/')); await p.waitForTimeout(2500);
  const fails = []; const ok = (c, m) => { if (!c) fails.push(m); };
  const counts = await p.evaluate(() => ({
    secs: document.querySelectorAll('details.example-diagrams').length,
    wraps: document.querySelectorAll('.k-scn-settings').length,
    gears: document.querySelectorAll('.k-scn-settings > .k-scn-gear').length,
    labels: Array.from(document.querySelectorAll('.k-scn-settings > .diagram-toggle > .k-this-scenario:first-child')).filter(x => x.textContent === 'This scenario:').length,
    flag: document.querySelectorAll('.k-flag').length,
    bodyFlag: document.body.classList.contains('k-scenario-settings'),
  }));
  console.log(browserName, 'counts', JSON.stringify(counts), 'inner', await p.evaluate(() => innerWidth + 'x' + innerHeight));
  ok(counts.wraps === counts.secs && counts.gears === counts.secs && counts.labels === counts.secs, 'a wrapper, gear and label per diagrams section');
  ok(counts.flag === 0 && !counts.bodyFlag, 'no report-wide switch');

  async function openScenario(name) {
    return p.evaluate(n => {
      const s = Array.from(document.querySelectorAll('details.scenario')).find(x => x.querySelector(':scope > summary').textContent.includes(n));
      let a = s; while (a) { if (a.tagName === 'DETAILS') a.open = true; a = a.parentElement; }
      const d = s.querySelector('details.example-diagrams'); d.open = true;
      s.setAttribute('data-probe', n);
      return !!d;
    }, name);
  }
  const NAME = 'A valid apple cinnamon muffin request should return a fresh batch';
  ok(await openScenario(NAME), 'scenario found');
  await p.waitForTimeout(2500);
  // the diagram's top a little below the report's bar
  await p.evaluate(n => {
    const s = document.querySelector('[data-probe="' + n + '"]');
    const w = s.querySelector('.k-scn-settings');
    const navH = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--kron-nav-h')) || 0;
    window.scrollBy(0, w.getBoundingClientRect().top - navH - 140);
  }, NAME);
  await p.waitForTimeout(600);
  const m = (n) => p.evaluate(n => {
    const s = document.querySelector('[data-probe="' + n + '"]');
    const w = s.querySelector('.k-scn-settings'), g = w.querySelector('.k-scn-gear'), bar = w.querySelector('.diagram-toggle');
    const r = g.getBoundingClientRect(), wr = w.getBoundingClientRect(), br = bar.getBoundingClientRect();
    const view = s.querySelector('.diagram-view:not([hidden]), .plantuml-browser');
    const vr = view ? view.getBoundingClientRect() : null;
    const sec = s.querySelector('details.example-diagrams').getBoundingClientRect();
    const cs = getComputedStyle(g);
    const ctrls = Array.from(bar.querySelectorAll('button, select, label')).filter(x => x.getBoundingClientRect().width > 0).map(x => { const q = x.getBoundingClientRect(); return [Math.round(q.top + q.height / 2), Math.round(q.right)]; });
    return { gear: [r.left, r.top, r.width, r.height].map(v => +v.toFixed(2)), wrap: [wr.top, wr.height].map(v => +v.toFixed(2)), barShown: br.height > 0,
      expanded: g.getAttribute('aria-expanded'), opacity: cs.opacity, color: cs.color, secRight: +sec.right.toFixed(2), viewTop: vr ? +vr.top.toFixed(2) : null,
      gearMid: +(r.top + r.height / 2).toFixed(2), ctrlMids: ctrls.slice(0, 6), firstChild: bar.firstElementChild.className + ':' + bar.firstElementChild.textContent,
      navH: getComputedStyle(document.documentElement).getPropertyValue('--kron-nav-h').trim(), title: g.title };
  }, n);
  const closed = await m(NAME);
  console.log('  closed', JSON.stringify(closed));
  ok(!closed.barShown && closed.expanded === 'false', 'closed: bar hidden');
  ok(Math.abs(closed.secRight - (closed.gear[0] + closed.gear[2])) < 16, 'closed: gear at the right edge');
  ok(+closed.opacity < 0.75, 'closed: gear greyed until pointed at');
  if (shots) await p.screenshot({ path: shots + '-' + browserName + '-closed.png', clip: { x: 0, y: Math.max(0, closed.gear[1] - 140), width: 1400, height: 420 } });
  // point at it: full strength
  await p.mouse.move(closed.gear[0] + closed.gear[2] / 2, closed.gear[1] + closed.gear[3] / 2);
  await p.waitForTimeout(300);
  const hov = await p.evaluate(n => getComputedStyle(document.querySelector('[data-probe="' + n + '"] .k-scn-gear')).opacity, NAME);
  ok(+hov > 0.95, 'hover: gear at full strength');
  const cx = closed.gear[0] + closed.gear[2] / 2, cy = closed.gear[1] + closed.gear[3] / 2;
  await p.mouse.click(cx, cy);
  await p.waitForTimeout(500);
  const open = await m(NAME);
  console.log('  open  ', JSON.stringify(open));
  ok(open.barShown && open.expanded === 'true', 'open: bar shown');
  ok(Math.abs(open.gear[0] - closed.gear[0]) < 0.6 && Math.abs(open.gear[1] - closed.gear[1]) < 0.6, 'open: gear did not move');
  ok(open.firstChild.indexOf('This scenario:') > 0, 'open: bar starts "This scenario:"');
  if (shots) await p.screenshot({ path: shots + '-' + browserName + '-open.png', clip: { x: 0, y: Math.max(0, open.gear[1] - 140), width: 1400, height: 420 } });
  // the settings act on sequence diagrams only: another tab hides the row, gear and all; back on Sequence it is open again
  const tabTo = (t) => p.evaluate(([n, t]) => { const s = document.querySelector('[data-probe="' + n + '"]'); s.querySelector('.diagram-toggle-btn[data-dtype="' + t + '"]').click(); }, [NAME, t]);
  await tabTo('activity'); await p.waitForTimeout(600);
  const act = await p.evaluate(n => { const s = document.querySelector('[data-probe="' + n + '"]'); return { gear: s.querySelector('.k-scn-gear').getBoundingClientRect().width, bar: s.querySelector('.k-scn-settings > .diagram-toggle').getBoundingClientRect().height }; }, NAME);
  ok(act.gear === 0 && act.bar === 0, 'activity tab: row hidden (' + JSON.stringify(act) + ')');
  await tabTo('seq'); await p.waitForTimeout(600);
  const seqBack = await m(NAME);
  ok(seqBack.barShown && seqBack.expanded === 'true', 'sequence tab again: still open');
  // others stay closed
  const othersOpen = await p.evaluate(() => document.querySelectorAll('.k-scn-settings.k-on').length);
  ok(othersOpen === 1, 'one scenario open, the rest closed (' + othersOpen + ')');
  // scroll down into the diagram: the bar sticks under the report's bar
  await p.mouse.wheel(0, 500); await p.waitForTimeout(700);
  const stuck = await m(NAME);
  console.log('  stuck ', JSON.stringify(stuck));
  const navH = parseFloat(stuck.navH) || 0;
  ok(Math.abs(stuck.wrap[0] - navH) < 1.5, 'scrolled: bar under the report bar (wrap top ' + stuck.wrap[0] + ' nav ' + navH + ')');
  if (shots) await p.screenshot({ path: shots + '-' + browserName + '-stuck.png', clip: { x: 0, y: 0, width: 1400, height: 300 } });
  // scroll back and close it from the same place
  await p.mouse.wheel(0, -500); await p.waitForTimeout(700);
  const back = await m(NAME);
  await p.mouse.click(back.gear[0] + back.gear[2] / 2, back.gear[1] + back.gear[3] / 2);
  await p.waitForTimeout(400);
  const shut = await m(NAME);
  ok(!shut.barShown && shut.expanded === 'false', 'second click closes it');
  // and a click at the very same point opens it again (no movement between the two)
  await p.mouse.click(back.gear[0] + back.gear[2] / 2, back.gear[1] + back.gear[3] / 2);
  await p.waitForTimeout(400);
  const again = await m(NAME);
  ok(again.barShown, 'a click at the same point opens it again');
  await p.mouse.click(back.gear[0] + back.gear[2] / 2, back.gear[1] + back.gear[3] / 2);
  await p.waitForTimeout(400);
  // a scenario with only a sequence diagram
  const seqOnly = await p.evaluate(() => {
    const d = Array.from(document.querySelectorAll('details.example-diagrams')).find(x => !x.querySelector(':scope > .diagram-toggle-tabs'));
    if (!d) return null;
    const s = d.closest('details.scenario');
    let a = s; while (a) { if (a.tagName === 'DETAILS') a.open = true; a = a.parentElement; }
    d.open = true; s.setAttribute('data-probe', 'seq-only');
    return s.querySelector(':scope > summary').textContent.trim().slice(0, 80);
  });
  console.log('  sequence-only scenario:', seqOnly);
  if (seqOnly) {
    await p.waitForTimeout(2000);
    await p.evaluate(() => { const w = document.querySelector('[data-probe="seq-only"] .k-scn-settings'); const navH = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--kron-nav-h')) || 0; window.scrollBy(0, w.getBoundingClientRect().top - navH - 140); });
    await p.waitForTimeout(600);
    const sc = await m('seq-only');
    await p.mouse.click(sc.gear[0] + sc.gear[2] / 2, sc.gear[1] + sc.gear[3] / 2);
    await p.waitForTimeout(500);
    const so = await m('seq-only');
    console.log('  seq-only closed', JSON.stringify(sc.gear), 'open', JSON.stringify(so.gear), so.firstChild);
    ok(!sc.barShown && so.barShown, 'sequence-only: opens');
    ok(Math.abs(so.gear[0] - sc.gear[0]) < 0.6 && Math.abs(so.gear[1] - sc.gear[1]) < 0.6, 'sequence-only: gear did not move');
    if (shots) await p.screenshot({ path: shots + '-' + browserName + '-seqonly.png', clip: { x: 0, y: Math.max(0, so.gear[1] - 140), width: 1400, height: 420 } });
  }
  ok(errors.length === 0, 'page errors: ' + errors.join(' | '));
  console.log(browserName, fails.length ? 'FAIL ' + fails.join('; ') : 'PASS');
  await b.close();
})();
