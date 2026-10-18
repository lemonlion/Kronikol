// Round 16: the Events and Setup toggles and the buttons of a collapsed payload that shows all of itself.
// 1. A scenario with both toggles: its diagram before, with Events hidden (no broker participant, fewer payloads),
//    shown again (as before), with Setup hidden (the first step is When, no Given), shown again.
// 2. With Setup hidden and every payload collapsed: the + of the first payload left opens that payload, and only it.
// 3. The report's Events toggle hides the brokers in an open scenario and turns every Events button to Hidden.
// 4. Where the toggles show: only in scenarios whose diagrams have events or setup.
// 5. Collapsed: a one-line payload ("1 row") offers no + and no expand; a longer one still does.
// usage: node toggles-test.js <page.local.html> [out dir]
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const path = require('path');
(async () => {
  const [page, out = 'shots/r16'] = process.argv.slice(2);
  const url = 'file:///' + path.resolve(page).split(path.sep).join('/');
  const b = await chromium.launch({ channel: 'chrome' });
  const ctx = await b.newContext({ viewport: { width: 1400, height: 900 } });
  await ctx.addInitScript(() => { try { localStorage.setItem('kronikol.theme', 'light'); localStorage.removeItem('kronikol.reportSettings'); } catch (e) {} });
  const p = await ctx.newPage();
  const errors = [];
  p.on('pageerror', e => errors.push(e.message));
  await p.goto(url); await p.waitForTimeout(2500);
  let fails = 0;
  const check = (name, ok, extra) => { if (!ok) fails++; console.log((ok ? 'ok   ' : 'FAIL ') + name + (extra ? ' ' + extra : '')); };
  const settle = async () => {
    await p.waitForTimeout(400);
    await p.waitForFunction(() => !document.querySelector('[data-probe] .details-pending, [data-probe] [data-pending]:not([data-pending="0"])') && !window._plantumlRendering, null, { polling: 200, timeout: 30000 }).catch(() => {});
    await p.waitForFunction(() => { const s = document.querySelector('[data-probe-seq] svg'); return s && s.getAttribute('data-k-roles'); }, null, { polling: 200, timeout: 30000 }).catch(() => {});
    await p.waitForTimeout(500);
  };
  const open = async (pick) => {
    const name = await p.evaluate(pick => {
      document.querySelectorAll('[data-probe]').forEach(e => e.removeAttribute('data-probe'));
      const all = Array.from(document.querySelectorAll('details.scenario'));
      const s = typeof pick === 'string' ? all.find(x => x.querySelector(':scope > summary').textContent.toLowerCase().includes(pick)) : all.find(x => x.querySelector('.toggle-btn[data-toggle="events"]') && x.querySelector('.toggle-btn[data-toggle="setup"]') && !x.querySelector('.param-test-table'));
      if (!s) return null;
      s.closest('details.feature').open = true; const r = s.closest('details.rule'); if (r) r.open = true; s.open = true;
      s.querySelectorAll('details').forEach(d => d.open = true); s.setAttribute('data-probe', '1');
      const row = s.querySelector('.param-test-table tbody tr'); if (row) row.click();
      return s.querySelector(':scope > summary').textContent.trim().replace(/\s+/g, ' ').slice(0, 70);
    }, pick);
    await p.waitForTimeout(400);
    await p.evaluate(() => {
      document.querySelectorAll('[data-probe-seq]').forEach(e => e.removeAttribute('data-probe-seq'));
      const seq = Array.from(document.querySelectorAll('[data-probe] .diagram-view-seq .plantuml-browser')).find(e => e.getBoundingClientRect().height > 0);
      if (seq) { seq.setAttribute('data-probe-seq', '1'); seq.scrollIntoView({ block: 'start' }); }
    });
    await settle();
    return name;
  };
  const look = () => p.evaluate(() => {
    const svg = document.querySelector('[data-probe-seq] svg');
    if (!svg) return null;
    const texts = Array.from(svg.querySelectorAll('text')).map(t => t.textContent);
    const steps = Array.from(svg.querySelectorAll('.k-step-text')).reduce((a, t) => { const y = t.getAttribute('y'); a[y] = (a[y] || '') + t.textContent; return a; }, {});
    return { notes: svg.querySelectorAll('.note-hover-rect').length, brokers: ['Kafka Broker', 'Azure Event Hub', 'Google Cloud Pub/Sub', 'Message Broker'].filter(n => texts.join(' ').includes(n)),
      steps: Object.keys(steps).sort((a, c) => a - c).map(k => steps[k].trim().split(' ')[0]), lifelines: svg.querySelectorAll('.k-lifeline').length };
  });
  const click = async (sel) => { await p.evaluate(sel => document.querySelector(sel).click(), sel); await settle(); };
  const label = sel => p.evaluate(sel => { const b = document.querySelector(sel); return b ? b.getAttribute('data-shown') + '|' + b.textContent.trim() : null; }, sel);

  // 1
  const name = await open(null);
  check('a scenario with both toggles', !!name, name);
  const before = await look();
  await click('[data-probe] .toggle-btn[data-toggle="events"]');
  const noEv = await look();
  check('Events hidden: no broker drawn', before.brokers.length > 0 && noEv.brokers.length === 0, JSON.stringify({ before: before.brokers, after: noEv.brokers }));
  check('Events hidden: fewer payloads', noEv.notes < before.notes, before.notes + ' -> ' + noEv.notes);
  check('Events button says Hidden', (await label('[data-probe] .toggle-btn[data-toggle="events"]')).startsWith('false'));
  await click('[data-probe] .toggle-btn[data-toggle="events"]');
  const back = await look();
  check('Events shown again: as before', JSON.stringify(back) === JSON.stringify(before), JSON.stringify(back));
  await p.screenshot({ path: out + '/toggles-before.png' });
  await click('[data-probe] .toggle-btn[data-toggle="setup"]');
  const noSet = await look();
  check('Setup hidden: first step is When', /^when$/i.test(noSet.steps[0] || ''), JSON.stringify(noSet.steps));
  check('Setup hidden: no Given step', !noSet.steps.some(s => /^given$/i.test(s)), JSON.stringify(before.steps));
  check('Setup hidden: fewer payloads', noSet.notes < before.notes, before.notes + ' -> ' + noSet.notes);
  await p.screenshot({ path: out + '/toggles-setup-hidden.png' });

  // 2: every payload collapsed, Setup still hidden: the first payload's + opens that one
  await p.evaluate(() => { const b = document.querySelector('[data-probe] .details-radio-btn[data-state="collapsed"]'); if (b) b.click(); });
  await settle();
  const heights = () => p.evaluate(() => Array.from(document.querySelectorAll('[data-probe-seq] svg .note-hover-rect')).map(r => Math.round(r.getBBox().height)));
  const h0 = await heights();
  // the first payload that is not whole (it has a +)
  const target = await p.evaluate(() => {
    const rects = Array.from(document.querySelectorAll('[data-probe-seq] svg .note-hover-rect'));
    for (let i = 0; i < rects.length; i++) {
      rects[i].dispatchEvent(new MouseEvent('mouseenter'));
      const shown = g => { const c = getComputedStyle(g); return c.display !== 'none' && c.visibility !== 'hidden' && +c.opacity > 0.05; };
      const plus = Array.from(document.querySelectorAll('[data-probe-seq] svg .note-toggle-icon')).find(g => shown(g) && g.querySelector('line') && !g.querySelector('text'));
      if (plus) { (plus.querySelector('rect') || plus).dispatchEvent(new MouseEvent('click', { bubbles: true })); return i; }
      rects[i].dispatchEvent(new MouseEvent('mouseleave'));
    }
    return -1;
  });
  await settle();
  const h1 = await heights();
  const grew = h1.map((h, i) => h > (h0[i] || 0) + 5 ? i : -1).filter(i => i >= 0);
  check('Setup hidden: a payload\'s + opens that payload and only it', target >= 0 && grew.length === 1 && grew[0] === target, 'clicked ' + target + ', grew ' + JSON.stringify(grew));
  await click('[data-probe] .toggle-btn[data-toggle="setup"]');
  await p.evaluate(() => { const b = document.querySelector('[data-probe] .details-radio-btn[data-state="truncated"]'); if (b) b.click(); });
  await settle();

  // 3: the report's toggle
  await click('.toolbar-right .toggle-btn[data-toggle="events"]');
  const rep = await look();
  const states = await p.evaluate(() => Array.from(document.querySelectorAll('.toggle-btn[data-toggle="events"]')).map(b => b.getAttribute('data-shown')));
  check('report Events hidden: the open scenario has no broker', rep.brokers.length === 0, JSON.stringify(rep.brokers));
  check('report Events hidden: every Events button says so', states.every(s => s === 'false'), states.length + ' buttons');
  await click('.toolbar-right .toggle-btn[data-toggle="events"]');
  const rep2 = await look();
  check('report Events shown again', rep2.brokers.length === before.brokers.length, JSON.stringify(rep2.brokers));

  // 4: where they show
  const where = await p.evaluate(() => {
    const sc = Array.from(document.querySelectorAll('details.scenario'));
    return { scenarios: sc.length, events: sc.filter(s => s.querySelector('.toggle-btn[data-toggle="events"]')).length, setup: sc.filter(s => s.querySelector('.toggle-btn[data-toggle="setup"]')).length,
      report: Array.from(document.querySelectorAll('.toolbar-right .toggle-btn')).map(b => b.getAttribute('data-toggle')) };
  });
  check('toggles only where they apply', where.events > 0 && where.events < where.scenarios && where.setup > 0 && where.setup <= where.scenarios, JSON.stringify(where));

  // 5: whole collapsed payloads
  await open('audit logs should be filterable by entity id');
  await p.evaluate(() => { const b = document.querySelector('[data-probe] .details-radio-btn[data-state="collapsed"]'); if (b) b.click(); });
  await settle();
  const rows = await p.evaluate(async () => {
    const out = [];
    const rects = Array.from(document.querySelectorAll('[data-probe-seq] svg .note-hover-rect'));
    for (const r of rects) {
      const b = r.getBBox();
      const svg = r.ownerSVGElement;
      const text = Array.from(svg.querySelectorAll('text')).filter(t => !t.closest('.note-toggle-icon')).filter(t => { const tb = t.getBBox(); return tb.x >= b.x - 1 && tb.x <= b.x + b.width && tb.y >= b.y - 1 && tb.y + tb.height <= b.y + b.height + 1; }).map(t => t.textContent).join('');
      r.dispatchEvent(new MouseEvent('mouseenter'));
      await new Promise(res => setTimeout(res, 350));
      // the buttons drawn on this payload (a neighbour's may still be fading out)
      const nb = r.getBoundingClientRect();
      const btns = Array.from(svg.querySelectorAll('.note-toggle-icon')).filter(g => {
        const c = getComputedStyle(g), gb = g.getBoundingClientRect();
        return c.display !== 'none' && c.visibility !== 'hidden' && +c.opacity > 0.5 && gb.right > nb.left - 4 && gb.left < nb.right + 4 && gb.bottom > nb.top - 4 && gb.top < nb.bottom + 4;
      }).length;
      r.dispatchEvent(new MouseEvent('mouseleave'));
      await new Promise(res => setTimeout(res, 500));
      out.push({ text: text.slice(0, 30), btns });
    }
    return out;
  });
  // a payload of one field is one line once opened (YAML, the default format: milk: Some_Fresh_Milk), so it is whole too
  const oneRow = rows.filter(r => r.text.trim() === '1 row' || /^"[^"]+": "[^"]*"$/.test(r.text.trim())), longer = rows.filter(r => /", "|\.\.\.$/.test(r.text.trim()));
  check('collapsed one-line payloads ("1 row", one field) offer no buttons', oneRow.length > 0 && oneRow.every(r => r.btns === 0), JSON.stringify(oneRow));
  check('collapsed longer payloads keep theirs', longer.length > 0 && longer.every(r => r.btns > 0), longer.length + ' payloads ' + JSON.stringify(longer.filter(r => !r.btns)));
  check('no page errors', errors.length === 0, errors.slice(0, 3).join(' | '));
  console.log(fails ? fails + ' FAILED' : 'ALL PASSED');
  await b.close();
})();
