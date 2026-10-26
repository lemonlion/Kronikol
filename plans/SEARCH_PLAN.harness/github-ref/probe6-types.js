// Probe 6: issues and users results, empty state, invalid query, code (logged out), loading state, sort URL params.
const { launch, newContext, save, installMeasure, sleep, OUT } = require('./lib');
const path = require('path');
const shot = (page, name, opts = {}) => page.screenshot({ path: path.join(OUT, name), ...opts });

const CENSUS = ({ sel: rootSel, n }) => {
  const m = window.__measure;
  const roots = Array.from(document.querySelectorAll(rootSel)).slice(0, n || 2);
  return roots.map((root) => {
    const seen = new Set();
    const rows = [];
    const walk = (el) => {
      for (const c of el.children) {
        const own = Array.from(c.childNodes).filter((t) => t.nodeType === 3 && t.textContent.trim()).map((t) => t.textContent.trim()).join(' ');
        const isVisual = ['svg', 'img'].includes(c.tagName.toLowerCase()) || /Label|Token|CounterLabel|Avatar|Button/.test(c.getAttribute('data-component') || '') || c.tagName === 'EM' || c.tagName === 'MARK';
        if (own || isVisual) {
          const r = m(c);
          const sig = [r.tag, r.fontSize, r.fontWeight, r.color, r.bg, r.radius, r.padding, r.border].join('|');
          if (!seen.has(sig) || c.tagName === 'EM' || c.tagName === 'MARK') {
            seen.add(sig);
            rows.push({ text: (own || r.text).slice(0, 50), tag: r.tag, dc: c.getAttribute('data-component'), cls: (typeof r.cls === 'string' ? r.cls : '').slice(0, 60), x: r.x, y: r.y, w: r.w, h: r.h, fs: r.fontSize, fw: r.fontWeight, lh: r.lineHeight, color: r.color, bg: r.bg, radius: r.radius, pad: r.padding, border: r.border, ff: r.fontFamily.slice(0, 30) });
          }
        }
        walk(c);
      }
    };
    walk(root);
    return { box: m(root), rows, html: root.outerHTML.replace(/<path d="[^"]*"/g, '<path d="…"').replace(/<svg[^>]*>(<path d="…"[^>]*><\/path>)+<\/svg>/g, '<svg…/>').slice(0, 9000) };
  });
};

const FACETS = () => ({
  facetTexts: Array.from(document.querySelectorAll('[data-testid="facets-pane"] [data-component="ActionList.Item"], [data-testid="facets-pane"] [class*="GroupHeading-"]')).map((li) => (li.innerText || '').trim().replace(/\s+/g, ' ')),
  subHeader: (document.querySelector('[data-testid="search-sub-header"]') || {}).innerText,
  title: document.title,
  url: location.href,
});

async function sortMenu(page) {
  await page.click('[data-testid="sort-button"]'); await sleep(700);
  const items = await page.evaluate(() => { const menu = Array.from(document.querySelectorAll('[role="menu"]')).find((e) => e.getBoundingClientRect().width > 0); return menu ? Array.from(menu.querySelectorAll('[role^="menuitem"]')).map((i) => (i.innerText || '').trim()) : null; });
  await page.keyboard.press('Escape'); await sleep(300);
  return items;
}

(async () => {
  const R = {};
  const browser = await launch();
  const ctx = await newContext(browser, { width: 1280, height: 900 });
  const page = await ctx.newPage();
  page.on('framenavigated', (f) => { if (f === page.mainFrame()) (R.navs = R.navs || []).push(f.url()); });

  // 1. Issues.
  await page.goto('https://github.com/search?q=playwright&type=issues', { waitUntil: 'load', timeout: 60000 });
  await sleep(3500); await installMeasure(page);
  R.issues = { facets: await page.evaluate(FACETS), items: await page.evaluate(CENSUS, { sel: '[data-testid="results-list"] > div', n: 2 }) };
  R.issues.sort = await sortMenu(page);
  await shot(page, 'results-issues-1280-light.png');
  await page.emulateMedia({ colorScheme: 'dark' }); await sleep(800);
  R.issues.dark = await page.evaluate(CENSUS, { sel: '[data-testid="results-list"] > div', n: 1 });
  await shot(page, 'results-issues-1280-dark.png');
  await page.emulateMedia({ colorScheme: 'light' }); await sleep(500);
  // Sort by "Newest": record the URL params.
  try {
    await page.click('[data-testid="sort-button"]'); await sleep(600);
    await page.locator('[role="menuitemradio"]').filter({ hasText: 'Newest' }).first().click(); await sleep(3000);
    R.issues.sortNewestUrl = page.url();
  } catch (e) { R.issues.sortErr = e.message; }

  // 2. Loading state: throttle, then soft-navigate to Users via the sidebar.
  const cdp = await ctx.newCDPSession(page);
  await cdp.send('Network.enable');
  await cdp.send('Network.emulateNetworkConditions', { offline: false, latency: 2500, downloadThroughput: 50 * 1024, uploadThroughput: 50 * 1024 });
  await page.click('[data-testid="nav-item-users"]');
  await sleep(700);
  await shot(page, 'loading-state-users-1280-light.png');
  R.loading = await page.evaluate(() => ({
    url: location.href,
    busy: Array.from(document.querySelectorAll('[aria-busy="true"]')).map((e) => e.tagName + '.' + String(e.className).slice(0, 60)),
    skeletons: Array.from(document.querySelectorAll('[class*="keleton"], [class*="Skeleton"], [class*="loading"], [class*="Loading"], [data-testid*="loading"]')).slice(0, 10).map((e) => ({ cls: String(e.className).slice(0, 80), tid: e.getAttribute('data-testid'), box: e.getBoundingClientRect().toJSON(), anim: getComputedStyle(e).animationName + ' ' + getComputedStyle(e).animationDuration, bg: window.__hex(getComputedStyle(e).backgroundColor) })),
    progress: Array.from(document.querySelectorAll('[role="progressbar"], .progress-pjax-loader, .turbo-progress-bar, [class*="ProgressBar"]')).map((e) => ({ cls: String(e.className).slice(0, 80), box: e.getBoundingClientRect().toJSON(), bg: window.__hex(getComputedStyle(e).backgroundColor) })),
    listOpacity: (() => { const l = document.querySelector('[data-testid="results-list"]'); return l ? getComputedStyle(l).opacity : null; })(),
  }));
  await sleep(1500);
  await shot(page, 'loading-state-users-1280-light-2.png');
  await cdp.send('Network.emulateNetworkConditions', { offline: false, latency: 0, downloadThroughput: -1, uploadThroughput: -1 });
  await sleep(6000);

  // 3. Users (soft-navigated).
  R.users = { facets: await page.evaluate(FACETS), items: await page.evaluate(CENSUS, { sel: '[data-testid="results-list"] > div', n: 2 }) };
  try { R.users.sort = await sortMenu(page); } catch (e) { R.users.sortErr = e.message; }
  await shot(page, 'results-users-1280-light.png');

  // 4. Pagination: page 2 (soft nav).
  try {
    await page.locator('nav[aria-label="Pagination"] a[aria-label="Next Page"]').first().click(); await sleep(3500);
    R.page2 = { url: page.url(), pages: await page.evaluate(() => Array.from(document.querySelectorAll('nav[aria-label="Pagination"] a, nav[aria-label="Pagination"] span')).map((e) => (e.innerText || '').trim() + (e.getAttribute('aria-current') ? '*' : '') + (e.getAttribute('aria-disabled') === 'true' ? '(disabled)' : ''))) };
  } catch (e) { R.page2Err = e.message; }
  await sleep(1500);

  // 5. Empty state (fresh load).
  await page.goto('https://github.com/search?q=zqxjvbnmpqwlkzt1x9&type=repositories', { waitUntil: 'load', timeout: 60000 });
  await sleep(3500); await installMeasure(page);
  R.empty = await page.evaluate(() => {
    const main = document.querySelector('[class*="mainColumn"]') || document.querySelector('main');
    const bs = document.querySelector('[data-component="Blankslate"], [class*="Blankslate"], [class*="blankslate"]');
    return { text: main ? main.innerText.trim().slice(0, 1500) : null, blankslate: bs ? window.__measure(bs) : null, html: (bs || main).outerHTML.replace(/<path d="[^"]*"/g, '<path d="…"').slice(0, 5000), facets: Array.from(document.querySelectorAll('[data-testid="kind-group"] [data-component="ActionList.Item"]')).map((li) => (li.innerText || '').trim().replace(/\s+/g, ' ')) };
  });
  R.empty.census = await page.evaluate(CENSUS, { sel: '[class*="mainColumn"]', n: 1 });
  await shot(page, 'empty-state-repos-1280-light.png');
  await sleep(1500);

  // 6. Invalid query (fresh load).
  await page.goto('https://github.com/search?q=' + encodeURIComponent('playwright (stars:>10') + '&type=repositories', { waitUntil: 'load', timeout: 60000 });
  await sleep(3500); await installMeasure(page);
  R.invalid1 = await page.evaluate(() => { const main = document.querySelector('[class*="mainColumn"]') || document.querySelector('main'); return { q: decodeURIComponent(location.search), text: main ? main.innerText.trim().slice(0, 1200) : null, flash: Array.from(document.querySelectorAll('[class*="flash"], [class*="Flash"], [data-component="Banner"], [class*="notice"], [role="alert"]')).map((e) => (e.innerText || '').trim().slice(0, 300)).filter(Boolean) }; });
  await shot(page, 'invalid-query-paren-1280-light.png');
  await sleep(1500);
  await page.goto('https://github.com/search?q=' + encodeURIComponent('playwright stars:>>abc created:2024-13-45') + '&type=repositories', { waitUntil: 'load', timeout: 60000 });
  await sleep(3500);
  R.invalid2 = await page.evaluate(() => { const main = document.querySelector('[class*="mainColumn"]') || document.querySelector('main'); return { q: decodeURIComponent(location.search), text: main ? main.innerText.trim().slice(0, 1200) : null, flash: Array.from(document.querySelectorAll('[class*="flash"], [class*="Flash"], [data-component="Banner"], [class*="notice"], [role="alert"]')).map((e) => (e.innerText || '').trim().slice(0, 300)).filter(Boolean) }; });
  await shot(page, 'invalid-query-range-1280-light.png');
  await sleep(1500);

  // 7. Code search, logged out (fresh load).
  await page.goto('https://github.com/search?q=' + encodeURIComponent('useState language:typescript') + '&type=code', { waitUntil: 'load', timeout: 60000 });
  await sleep(3500); await installMeasure(page);
  R.code = await page.evaluate(() => { const main = document.querySelector('[class*="mainColumn"]') || document.querySelector('main'); return { url: location.href, title: document.title, text: main ? main.innerText.trim().slice(0, 1500) : null, html: main ? main.outerHTML.replace(/<path d="[^"]*"/g, '<path d="…"').slice(0, 5000) : null }; });
  await shot(page, 'code-search-loggedout-1280-light.png');

  save('probe6-types.json', R);
  console.log(JSON.stringify({ navs: R.navs, issuesSort: R.issues.sort, issuesSortUrl: R.issues.sortNewestUrl, usersSort: R.users && R.users.sort, page2: R.page2, loading: R.loading, emptyText: R.empty.text.slice(0, 400), invalid1: R.invalid1, invalid2: R.invalid2, code: R.code.text && R.code.text.slice(0, 500) }, null, 1));
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
