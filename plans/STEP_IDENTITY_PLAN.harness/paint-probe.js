// What the page paints for a scenario, not what the data file holds.
// usage: node paint-probe.js <TestRunReport.html> <label> <out-dir> <stableId>...
const path = require('path');
const fs = require('fs');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require(path.resolve(__dirname, '../../tools/playwright/installed-browser.js'));

const BAR = /^(Given|When|Then|And|Reference|Seeding|A tenant|Block)/;

(async () => {
  const [report, label, outDir, ...ids] = process.argv.slice(2);
  fs.mkdirSync(outDir, { recursive: true });
  const browser = await launchInstalled(chromium, { headless: true });
  const page = await browser.newPage({ viewport: { width: 1280, height: 1400 } });
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message.split('\n')[0]));
  await page.goto('file:///' + path.resolve(report).replace(/\\/g, '/'));
  await page.waitForFunction(() => document.querySelectorAll('[data-stable-id]').length > 0, null, { polling: 200, timeout: 60000 });
  const census = await page.evaluate(() => ({
    scenarios: document.querySelectorAll('details.scenario').length,
    stableIds: document.querySelectorAll('[data-stable-id]').length,
  }));
  console.log(`=== ${label}: ${census.scenarios} details.scenario, ${census.stableIds} with a stable id`);

  for (const id of ids) {
    const handle = await page.$(`[data-stable-id="${id}"]`);
    if (!handle) {
      console.log(`--- ${label} ${id}: NOT ON THE PAGE`);
      continue;
    }

    const found = await handle.evaluate((scenario) => {
      for (let p = scenario; p; p = p.parentElement) if (p.tagName === 'DETAILS') p.open = true;
      scenario.scrollIntoView();
      scenario.querySelectorAll('details').forEach((d) => { d.open = true; });
      const line = (e) => e.textContent.trim().replace(/\s+/g, ' ').slice(0, 72);
      const depth = (e) => {
        let n = 0;
        for (let p = e.parentElement; p && p !== scenario; p = p.parentElement) if (p.classList.contains('step')) n++;
        return n;
      };
      return {
        title: scenario.querySelector('summary') ? line(scenario.querySelector('summary')) : scenario.tagName,
        rows: [...scenario.querySelectorAll('.step')].map((step) => {
          const pick = (selector) => {
            const hit = [...step.querySelectorAll(selector)].find((c) => c.closest('.step') === step);
            return hit ? line(hit) : '';
          };
          return `${'  '.repeat(depth(step))}${pick('.step-status')} ${pick('.step-keyword')} ${pick('.step-text')}`.trimEnd();
        }),
      };
    });

    await page.waitForFunction((s) => {
      const el = document.querySelector(s);
      return !!(el && el.querySelector('svg text'));
    }, `[data-stable-id="${id}"]`, { polling: 200, timeout: 120000 }).catch(() => {});
    const bars = await handle.evaluate((scenario, source) => {
      const firstText = scenario.querySelector('svg text'); const svg = firstText && firstText.closest('svg');
      if (!svg) return null;
      const bar = new RegExp(source);
      const all = [...svg.querySelectorAll('text')].map((t) => t.textContent.trim()); const hit = all.filter((t) => bar.test(t)); return hit.length ? hit : { texts: all.length, first: all.slice(0, 8) };
    }, BAR.source);

    console.log(`--- ${label} ${id}: ${found.title}`);
    for (const row of found.rows) console.log('  row  ' + row);
    console.log('  bars ' + JSON.stringify(bars));
    await handle.screenshot({ path: path.join(outDir, `${label}-${id}.png`) })
      .catch((e) => console.log('  (no screenshot: ' + e.message.split('\n')[0] + ')'));
  }

  if (errors.length) console.log('page errors: ' + JSON.stringify([...new Set(errors)]));
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
