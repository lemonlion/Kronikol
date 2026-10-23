'use strict';
// Times the report-level toggles (the toolbar that floats above an open scenario) on a real report.
// usage: node float-bench.js <report.html> <feature title substring> <scenarios: 0|1|2|all> [toggles] [rounds] [--profile=<toggle>] [--width=W --height=H]
//   Opens the feature and the chosen scenario(s), waits until every diagram the page draws for them is drawn, scrolls
//   the first opened scenario's first diagram under the floating bar, then switches each toggle and back, `rounds` times.
// Per switch: handlerMs (the click's own task), frameMs (click -> next frame painted), screenMs (click -> every diagram
// on screen holds a new picture), doneMs (click -> no control pending), longest/total long task, redrawn count.
const path = require('path');
const fs = require('fs');
// written for the 5.5.1 work (the toggles on one long diagram), copied here for M9; launchInstalled as CLAUDE.md asks
const PW = require(process.env.PLAYWRIGHT_CORE || 'playwright-core');
const { launchInstalled } = require(path.join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));

const args = process.argv.slice(2);
const flags = Object.fromEntries(args.filter((a) => a.startsWith('--')).map((a) => { const [k, v] = a.slice(2).split('='); return [k, v === undefined ? true : v]; }));
const [file, featureText, which = '0', toggleList = 'assertions,steps,databases,setup', roundsArg = '2'] = args.filter((a) => !a.startsWith('--'));
const rounds = Number(roundsArg);

async function settle(page, quietMs, maxMs) {
  return page.evaluate(({ quietMs, maxMs }) => new Promise((resolve) => {
    const t0 = performance.now();
    let last = window._renderCompleteCount || 0, lastAt = performance.now();
    const tick = () => {
      const now = performance.now(), c = window._renderCompleteCount || 0;
      if (c !== last) { last = c; lastAt = now; }
      const lt = window.__lt.length ? window.__lt[window.__lt.length - 1] : null;
      const ltQuiet = !lt || now - (lt.s + lt.d) >= 700;
      const busy = window._plantumlRendering || document.querySelector('[data-pending]');
      if ((!busy && ltQuiet && now - lastAt >= quietMs) || now - t0 > maxMs) return resolve(Math.round(now - t0));
      setTimeout(tick, 100);
    };
    setTimeout(tick, 100);
  }), { quietMs, maxMs });
}

(async () => {
  const browser = await launchInstalled(PW.chromium, { headless: true });
  try {
    const ctx = await browser.newContext({ viewport: { width: Number(flags.width || 1920), height: Number(flags.height || 1080) } });
    const page = await ctx.newPage();
    const errors = [];
    page.on('pageerror', (e) => errors.push(String(e).slice(0, 300)));
    await page.addInitScript(() => {
      window.__lt = [];
      try {
        new PerformanceObserver((l) => l.getEntries().forEach((e) => window.__lt.push({ s: e.startTime, d: e.duration })))
          .observe({ type: 'longtask', buffered: true });
      } catch (e) { /* no long tasks API */ }
    });
    await page.goto('file:///' + path.resolve(file).split(path.sep).join('/'), { timeout: 300000 });
    await settle(page, 1500, 120000);
    const opened = await page.evaluate(({ featureText, which }) => {
      const f = Array.from(document.querySelectorAll('details.feature')).find((d) => {
        const s = d.querySelector(':scope > summary'); return s && s.textContent.indexOf(featureText) >= 0;
      });
      f.open = true;
      const scen = Array.from(f.querySelectorAll('details.scenario'));
      const pick = which === 'all' ? scen : [scen[Number(which)]];
      pick.forEach((s) => { s.open = true; });
      window.__scen = pick;
      pick[0].scrollIntoView({ block: 'start' });
      return pick.map((s) => s.id);
    }, { featureText, which });
    // let the page draw what it draws for the opened scenario(s) as they come into view: walk down through them
    for (let step = 0; step < 40; step++) {
      await settle(page, 1200, 300000);
      const more = await page.evaluate(() => {
        const last = window.__scen[window.__scen.length - 1];
        const r = last.getBoundingClientRect();
        if (r.bottom <= window.innerHeight) return false;
        window.scrollBy(0, Math.round(window.innerHeight * 0.8));
        return true;
      });
      if (!more) break;
    }
    await settle(page, 1500, 300000);
    // the first opened scenario's first drawn diagram, just under the floating bar
    const state = await page.evaluate(() => {
      const s = window.__scen[0];
      const d = s.querySelector('.plantuml-browser[data-rendered] svg') ? s.querySelector('.plantuml-browser[data-rendered]') : s;
      const nav = document.querySelector('.toolbar-row.report-nav');
      const navH = nav ? nav.getBoundingClientRect().height : 0;
      window.scrollTo(0, window.scrollY + d.getBoundingClientRect().top - navH - 20);
      const all = Array.from(document.querySelectorAll('.plantuml-browser'));
      const drawn = all.filter((c) => c.querySelector('svg'));
      const inScen = window.__scen.map((sc) => {
        const ds = Array.from(sc.querySelectorAll('.plantuml-browser'));
        return {
          id: sc.id.slice(0, 60),
          diagrams: ds.length,
          drawn: ds.filter((c) => c.querySelector('svg')).length,
          drawnInfo: ds.filter((c) => c.querySelector('svg')).map((c) => {
            const svgs = c.querySelectorAll('svg');
            let nodes = 0, texts = 0; svgs.forEach((v) => { nodes += v.getElementsByTagName('*').length; texts += v.getElementsByTagName('text').length; });
            return { src: (c.getAttribute('data-plantuml') || '').length, parts: c.querySelectorAll(':scope > .puml-fragment').length, svgNodes: nodes, texts, h: Math.round(c.getBoundingClientRect().height) };
          })
        };
      });
      return { drawnOnPage: drawn.length, withSource: document.querySelectorAll('[data-plantuml]').length, inScen,
        floating: nav ? nav.classList.contains('k-floating') + '/' + nav.classList.contains('is-pinned') : 'no nav', scrollY: Math.round(window.scrollY) };
    });
    await settle(page, 1000, 60000);
    console.log(JSON.stringify({ opened, state }));

    let cdp = null;
    for (let round = 0; round < rounds; round++) {
      for (const name of toggleList.split(',')) {
        for (const phase of ['switch', 'back']) {
          const profiling = flags.profile === name && round === rounds - 1 && phase === 'switch';
          let events = [];
          if (profiling) {
            cdp = cdp || await ctx.newCDPSession(page);
            await cdp.send('Profiler.enable');
            await cdp.send('Profiler.setSamplingInterval', { interval: 100 });
            cdp.on('Tracing.dataCollected', (d) => events.push(...d.value));
            await cdp.send('Tracing.start', { categories: 'devtools.timeline,blink.user_timing,v8.execute', transferMode: 'ReportEvents' });
            await cdp.send('Profiler.start');
          }
          // the diagrams on screen, by hit testing, in a task of its own (it took 500 ms on a long diagram)
          await page.evaluate(() => {
            document.querySelectorAll('.plantuml-browser svg').forEach((s) => { s._before = true; });
            const onScreen = new Set();
            for (let y = 5; y < window.innerHeight; y += 15) for (let x = 40; x < window.innerWidth; x += 120) {
              const e = document.elementFromPoint(x, y), c = e && e.closest && e.closest('.plantuml-browser');
              if (c && c.querySelector('svg')) onScreen.add(c);
            }
            window.__screen = Array.from(onScreen);
          });
          await page.waitForTimeout(300);
          const r = await page.evaluate((name) => new Promise((resolve) => {
            const btn = Array.from(document.querySelectorAll('.toggle-btn[data-toggle="' + name + '"]')).find((b) => !b.closest('details.scenario'));
            const screen = window.__screen;
            const nLt = window.__lt.length;
            const rc0 = window._renderCompleteCount || 0;
            const tel0 = Object.assign({}, window.__kronikolRender || {});
            let screenAt = screen.length ? null : 0, frameAt = null;
            const t = performance.now();
            requestAnimationFrame(() => setTimeout(() => { frameAt = performance.now(); }, 0));
            btn.click();
            const handlerMs = performance.now() - t;
            (function poll() {
              const now = performance.now();
              if (screenAt === null && screen.every((c) => { const s = c.querySelector('svg'); return s && !s._before; })) screenAt = now;
              const done = !document.querySelector('[data-pending]') && (window._renderCompleteCount || 0) > rc0;
              if ((done && screenAt !== null && frameAt !== null) || now - t > 120000) {
                const doneAt = now;
                // let late long tasks land
                setTimeout(() => {
                  const tasks = window.__lt.slice(nLt).filter((x) => x.s + x.d >= t);
                  const drawn = Array.from(document.querySelectorAll('.plantuml-browser')).filter((c) => c.querySelector('svg'));
                  resolve({
                    shown: btn.getAttribute('data-shown'),
                    handlerMs: Math.round(handlerMs), frameMs: Math.round(frameAt - t),
                    screenMs: screenAt === null ? null : Math.round(screenAt - t), doneMs: Math.round(doneAt - t),
                    onScreen: screen.length,
                    longest: Math.round(tasks.reduce((m, x) => Math.max(m, x.d), 0)),
                    longTotal: Math.round(tasks.reduce((s, x) => s + x.d, 0)), longN: tasks.length,
                    tasks: tasks.map((x) => Math.round(x.s - t) + '+' + Math.round(x.d)),
                    redrawn: drawn.filter((c) => { const s = c.querySelector('svg'); return s && !s._before; }).length,
                    drawn: drawn.length,
                    engine: (function () { const t1 = window.__kronikolRender || {}; return { renders: (t1.renders || 0) - (tel0.renders || 0), workerMs: Math.round((t1.workerMs || 0) - (tel0.workerMs || 0)), injectMs: Math.round((t1.injectMs || 0) - (tel0.injectMs || 0)), cacheHits: (t1.cacheHits || 0) - (tel0.cacheHits || 0) }; })(),
                    // a marked copy's performance marks since the click, for the diagram(s) on screen first
                    marks: performance.getEntriesByType('mark').filter((m) => m.startTime >= t).slice(0, 40)
                      .map((m) => Math.round(m.startTime - t) + ' ' + m.name.replace(/scenario-|-frag-n?-?/g, (x) => x === 'scenario-' ? '' : x).slice(0, 70)),
                    screenIds: screen.map((c) => c.id.slice(0, 50))
                  });
                  performance.clearMarks();
                }, 300);
                return;
              }
              setTimeout(poll, 4);
            })();
          }), name);
          console.log(JSON.stringify({ round, toggle: name, phase, ...r }));
          if (profiling) {
            await page.waitForTimeout(500);
            const { profile } = await cdp.send('Profiler.stop');
            const traced = new Promise((res) => cdp.once('Tracing.tracingComplete', res));
            await cdp.send('Tracing.end');
            await traced;
            fs.writeFileSync(path.join(__dirname, 'out', 'profile-' + name + '.cpuprofile'), JSON.stringify(profile));
            fs.writeFileSync(path.join(__dirname, 'out', 'trace-' + name + '.json'), JSON.stringify(events));
            console.log('profile written: out/profile-' + name + '.cpuprofile (' + events.length + ' trace events)');
          }
          await settle(page, 800, 120000);
        }
      }
    }
    if (errors.length) console.log('page errors: ' + JSON.stringify(errors.slice(0, 5)));
  } finally {
    await browser.close();
  }
})().catch((e) => { console.error(e); process.exit(1); });
