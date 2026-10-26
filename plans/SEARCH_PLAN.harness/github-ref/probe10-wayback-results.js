// Probe 10: render the archived (2025-06-28) logged-out search results page through the Wayback Machine.
// Shows how the Catalyst header search displays the current query, and what the "Advanced" facet items do.
// No github.com requests: live GitHub hosts are blocked.
const { launch, newContext, save, installMeasure, sleep, OUT } = require('./lib');
const path = require('path');
const URL = 'https://web.archive.org/web/20250628003312if_/https://github.com/search?q=react&type=repositories';
const shot = (page, name, opts = {}) => page.screenshot({ path: path.join(OUT, name), ...opts });

(async () => {
  const R = { url: URL };
  const browser = await launch();
  const ctx = await newContext(browser, { width: 1280, height: 900 });
  const page = await ctx.newPage();
  await page.route(/^https:\/\/(github\.com|api\.github\.com|collector\.github\.com|github\.githubassets\.com|avatars\.githubusercontent\.com)\//, (r) => r.abort());
  await page.goto(URL, { waitUntil: 'load', timeout: 120000 });
  await sleep(7000);
  await installMeasure(page);
  R.header = await page.evaluate(() => {
    const qb = document.querySelector('qbsearch-input');
    const b = qb && qb.querySelector('.header-search-button');
    return { qbAttrs: qb ? Array.from(qb.attributes).map((a) => a.name + '=' + a.value.slice(0, 60)) : null, button: window.__measure(b), buttonHtml: b ? b.outerHTML.replace(/<path d="[^"]*"/g, '<path d="…"').slice(0, 2500) : null, container: window.__measure(qb && qb.querySelector('.search-input-container')), textEl: window.__measure(b && b.querySelector('[data-target="qbsearch-input.inputButtonText"]')) };
  });
  await shot(page, 'wayback-20250628-results-header-1280-light.png', { clip: { x: 0, y: 0, width: 1280, height: 80 } });
  await shot(page, 'wayback-20250628-results-1280-light.png');
  R.facets = await page.evaluate(() => Array.from(document.querySelectorAll('[data-testid="facets-pane"] [data-component="ActionList.Item"], [data-testid="facets-pane"] li')).map((li) => (li.innerText || '').trim().replace(/\s+/g, ' ')).filter(Boolean).slice(0, 60));
  // Click "Owner" in the Advanced group, if present.
  try {
    const owner = page.locator('[data-testid="facets-pane"] button, [data-testid="facets-pane"] [role="button"], [data-testid="facets-pane"] a').filter({ hasText: /^\s*Owner\s*$/ }).first();
    await owner.click({ timeout: 8000 });
    await sleep(1500);
    R.afterOwner = await page.evaluate(() => {
      const qb = document.querySelector('qbsearch-input');
      const input = qb && qb.querySelector('input[role="combobox"]');
      const cont = qb && qb.querySelector('.query-builder-container');
      return { open: cont ? !cont.hidden : null, value: input && input.value, caret: input && input.selectionStart, focused: document.activeElement === input, styled: qb && (qb.querySelector('.QueryBuilder-StyledInputContent') || {}).innerHTML, items: qb ? Array.from(qb.querySelectorAll('ul[role="listbox"] [role="option"]')).map((o) => (o.innerText || '').trim().replace(/\s+/g, ' ')).slice(0, 12) : null, groups: qb ? Array.from(qb.querySelectorAll('.QueryBuilder-sectionTitle')).map((h) => h.textContent.trim()) : null, container: window.__measure(cont) };
    });
    await shot(page, 'wayback-20250628-advanced-owner-click-1280-light.png', { clip: { x: 0, y: 0, width: 1280, height: 520 } });
    await page.keyboard.press('Escape'); await sleep(600);
  } catch (e) { R.ownerErr = e.message.slice(0, 200); }
  // Open the header search with "/": it should hold the current query.
  try {
    await page.mouse.click(700, 700); await page.keyboard.press('/'); await sleep(1500);
    R.slash = await page.evaluate(() => { const qb = document.querySelector('qbsearch-input'); const input = qb && qb.querySelector('input[role="combobox"]'); const cont = qb && qb.querySelector('.query-builder-container'); return { open: cont ? !cont.hidden : null, value: input && input.value, items: qb ? Array.from(qb.querySelectorAll('ul[role="listbox"] [role="option"]')).map((o) => (o.innerText || '').trim().replace(/\s+/g, ' ')).slice(0, 12) : null, container: window.__measure(cont), live: Array.from(qb.querySelectorAll('[aria-live]')).map((e) => e.textContent.trim()) }; });
    await shot(page, 'wayback-20250628-qb-open-on-results-1280-light.png', { clip: { x: 0, y: 0, width: 1280, height: 520 } });
  } catch (e) { R.slashErr = e.message.slice(0, 200); }
  save('probe10-wayback-results.json', R);
  console.log(JSON.stringify({ header: { attrs: R.header.qbAttrs, button: R.header.button && [R.header.button.x, R.header.button.w, R.header.button.h, R.header.button.text], text: R.header.textEl && [R.header.textEl.display, R.header.textEl.text] }, facets: R.facets && R.facets.slice(0, 40), afterOwner: R.afterOwner, ownerErr: R.ownerErr, slash: R.slash, slashErr: R.slashErr }, null, 1));
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
