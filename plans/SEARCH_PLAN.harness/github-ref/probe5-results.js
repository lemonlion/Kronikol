// Probe 5: the repositories results page, a fresh load at each width (4 page loads).
const { launch, newContext, save, installMeasure, sleep, OUT } = require('./lib');
const path = require('path');

const URL = 'https://github.com/search?q=playwright&type=repositories';
const WIDTHS = (process.argv[2] || '1280,1012,768,390').split(',').map(Number);
const shot = (page, name, opts = {}) => page.screenshot({ path: path.join(OUT, name), ...opts });

const MEASURE_RESULTS = () => {
  const m = window.__measure;
  const hex = window.__hex;
  const q = (s, r = document) => r.querySelector(s);
  const qa = (s, r = document) => Array.from(r.querySelectorAll(s));
  const pseudo = (el, which) => {
    if (!el) return null;
    const cs = getComputedStyle(el, which);
    if (!cs || cs.content === 'none' || cs.content === 'normal') return null;
    return { content: cs.content, position: cs.position, w: cs.width, h: cs.height, top: cs.top, left: cs.left, bg: hex(cs.backgroundColor), radius: cs.borderRadius, display: cs.display };
  };
  const out = { vw: innerWidth };
  out.header = m('header');
  out.layoutRoot = m('[data-component="SplitPageLayout"]');
  out.paneWrapper = m('[class*="PageLayout-PaneWrapper"]');
  out.pane = m('[data-component="PageLayout.Pane"]');
  out.paneStyle = (() => { const p = q('[data-component="PageLayout.Pane"]'); if (!p) return null; const cs = getComputedStyle(p); return { width: cs.width, minWidth: cs.minWidth, maxWidth: cs.maxWidth, paneWidthVar: cs.getPropertyValue('--pane-width'), padding: cs.padding, position: cs.position, top: cs.top, height: cs.height, overflow: cs.overflowY }; })();
  out.verticalDivider = m('[data-component="PageLayout.VerticalDivider"]');
  out.horizontalDivider = m('[data-component="PageLayout.HorizontalDivider"]');
  out.contentWrapper = m('[data-component="SplitPageLayout.Content"]');
  out.content = m('[class*="PageLayout-Content-"]');
  const two = q('[class*="twoColumnOuter"]');
  out.twoColumnOuter = m(two);
  out.twoColumnChildren = two ? Array.from(two.children).map(m) : null;
  out.mainColumn = m('[class*="mainColumn"]');
  out.filtersTitle = m('#search-filters-title');
  out.navItems = qa('[data-testid="kind-group"] [data-component="ActionList.Item"]').map((li) => {
    const c = li.firstElementChild;
    const r = m(li);
    r.content = m(c);
    r.ariaCurrent = c && c.getAttribute('aria-current');
    r.liAfter = pseudo(li, '::after'); r.liBefore = pseudo(li, '::before');
    r.cAfter = pseudo(c, '::after'); r.cBefore = pseudo(c, '::before');
    r.counter = m(li.querySelector('[data-component="CounterLabel"]'));
    r.leading = m(li.querySelector('[data-component="ActionList.LeadingVisual"]'));
    r.label = m(li.querySelector('[data-component="ActionList.Item.Label"]'));
    return r;
  });
  out.groupHeadings = qa('[data-testid="facets-pane"] [class*="GroupHeading-"]').map(m);
  out.languageItems = qa('[data-testid="facets-pane"] [class*="FacetOption"]').slice(0, 3).map((li) => ({ li: m(li), content: m(li.firstElementChild), dot: m(li.querySelector('[class*="languageCircle"]')), dotStyle: (() => { const d = li.querySelector('[class*="languageCircle"]'); if (!d) return null; const cs = getComputedStyle(d); const b = getComputedStyle(d, '::before'); return { w: cs.width, h: cs.height, bg: hex(cs.backgroundColor), radius: cs.borderRadius, before: b.content !== 'none' ? { w: b.width, h: b.height, bg: hex(b.backgroundColor), radius: b.borderRadius } : null, inline: d.getAttribute('style') }; })() }));
  out.facetTexts = qa('[data-testid="facets-pane"] [data-component="ActionList.Item"]').map((li) => (li.innerText || '').trim().replace(/\s+/g, ' '));
  out.advancedSearchLink = m('[data-testid="facets-pane"] a[href="/search/advanced"]');
  out.dividers = qa('[data-testid="facets-pane"] [data-component="ActionList.Divider"]').map(m);
  // Results header.
  const sub = q('[data-testid="search-sub-header"]');
  out.subHeader = m(sub);
  out.subHeaderHtml = sub ? sub.outerHTML.replace(/<path d="[^"]*"/g, '<path d="…"').slice(0, 6000) : null;
  out.countTexts = sub ? qa('span, h2, div', sub).filter((e) => /results/.test(e.textContent) && e.children.length <= 3).slice(0, 6).map(m) : null;
  out.sortButton = m('[data-testid="sort-button"]');
  out.filterButton = m('[data-testid="filter-button"]');
  out.typeSwitcher = m('[data-testid="type-switcher"]');
  out.columnOptions = qa('button[aria-label="Open column options"]').map(m);
  // Result items.
  const items = qa('[data-testid="results-list"] > div');
  out.resultsList = m('[data-testid="results-list"]');
  out.resultCount = items.length;
  out.results = items.slice(0, 3).map((it) => ({
    item: m(it), row: m(it.querySelector('[class*="resultRow"]')),
    content: m(it.querySelector('[class*="resultContent"]')),
    h3: m(it.querySelector('h3')), avatar: m(it.querySelector('img[data-component="Avatar"]')),
    titleLink: m(it.querySelector('.search-title a')), titleEm: m(it.querySelector('.search-title em')),
    desc: m(it.querySelector('[class*="Content__"]')), descEm: m(it.querySelector('[class*="Content__"] em')),
    topic: m(it.querySelector('[class*="topicLabel"]')), topicList: m(it.querySelector('[class*="tokenList"]')),
    meta: m(it.querySelector('ul[class*="footer"]')), metaItem: m(it.querySelector('ul[class*="footer"] li')),
    metaDot: m(it.querySelector('ul[class*="footer"] [class*="languageCircle"]')),
    starLink: m(it.querySelector('[class*="stargazersLink"]')),
    starButton: m(it.querySelector('[class*="starButtonWrapper"] [data-component="Button"]')),
  }));
  out.firstResultHtml = items[0] ? items[0].outerHTML.replace(/<path d="[^"]*"/g, '<path d="…"') : null;
  out.gapBetweenResults = items.length > 1 ? +(items[1].getBoundingClientRect().top - items[0].getBoundingClientRect().bottom).toFixed(2) : null;
  // Pagination.
  out.pagination = m('nav[aria-label="Pagination"]');
  out.pages = qa('nav[aria-label="Pagination"] a, nav[aria-label="Pagination"] span, nav[aria-label="Pagination"] em').filter((e) => e.children.length === 0 || e.tagName === 'A').map((e) => { const r = m(e); r.ariaCurrent = e.getAttribute('aria-current'); r.ariaLabel = e.getAttribute('aria-label'); r.ariaDisabled = e.getAttribute('aria-disabled'); return r; });
  // Right column.
  out.proTip = m(qa('[class*="twoColumnOuter"] *').find((e) => e.children.length < 6 && /ProTip!/.test(e.textContent) && e.textContent.length < 160) || null);
  out.root = { bg: hex(getComputedStyle(document.body).backgroundColor), fg: hex(getComputedStyle(document.body).color), font: getComputedStyle(document.body).fontFamily, size: getComputedStyle(document.body).fontSize };
  out.docScrollWidth = document.documentElement.scrollWidth;
  return out;
};

async function openHamburgerAndSearch(page, w, results) {
  // Narrow widths: the search trigger lives in the hamburger menu.
  const info = await page.evaluate(() => {
    const vis = (e) => { const r = e.getBoundingClientRect(); return r.width > 0 && r.height > 0 && e.checkVisibility({ checkOpacity: true, checkVisibilityCSS: true }); };
    const btns = Array.from(document.querySelectorAll('header button')).filter(vis);
    return btns.map((b, i) => ({ i, label: b.getAttribute('aria-label') || b.innerText.trim(), expanded: b.getAttribute('aria-expanded'), x: b.getBoundingClientRect().x, w: b.getBoundingClientRect().width }));
  });
  results.headerButtonsNarrow = info;
  const ham = info.find((b) => /menu|navigation/i.test(b.label || '')) || info[0];
  if (!ham) return;
  await page.locator('header button').filter({ visible: true }).nth(info.indexOf(ham)).click({ timeout: 5000 });
  await sleep(1000);
  await shot(page, `header-menu-open-${w}-light.png`);
  results.hamburger = { label: ham.label };
  results.searchInMenu = await page.evaluate(() => {
    const b = Array.from(document.querySelectorAll('button[aria-label*="Search or jump to"]')).find((e) => e.checkVisibility({ checkOpacity: true, checkVisibilityCSS: true }));
    if (!b) return null;
    const r = window.__measure(b);
    r.label = window.__measure(b.querySelector('[class^="label__"], [class*=" label__"]'));
    r.kbd = window.__measure(b.querySelector('kbd'));
    return r;
  });
  if (results.searchInMenu) {
    await page.locator('button[aria-label*="Search or jump to"]').filter({ visible: true }).first().click({ timeout: 5000 });
    await sleep(1200);
    results.dialogNarrow = await page.evaluate(() => {
      const d = Array.from(document.querySelectorAll('[role="dialog"][data-component="Dialog"]')).find((e) => e.getBoundingClientRect().width > 0);
      if (!d) return null;
      const r = window.__measure(d);
      const cs = getComputedStyle(d);
      r.anim = cs.animationName + ' ' + cs.animationDuration;
      r.backdrop = window.__hex(getComputedStyle(d.parentElement).backgroundColor);
      r.input = window.__measure(d.querySelector('.TextInput-wrapper'));
      r.vars = d.getAttribute('style');
      r.firstOption = window.__measure(d.querySelector('[role="option"]'));
      return r;
    });
    await shot(page, `qb-empty-${w}-light.png`);
    await page.keyboard.press('Escape');
    await sleep(600);
  }
}

(async () => {
  const all = {};
  const browser = await launch();
  for (const w of WIDTHS) {
    const r = { errors: [] };
    const ctx = await newContext(browser, { width: w, height: 900 });
    const page = await ctx.newPage();
    await page.goto(URL, { waitUntil: 'load', timeout: 60000 });
    await sleep(3500);
    await installMeasure(page);
    r.m = await page.evaluate(MEASURE_RESULTS);
    await shot(page, `results-repos-${w}-light.png`);
    if (w === 1280) {
      // Sort menu.
      try {
        await page.click('[data-testid="sort-button"]'); await sleep(700);
        r.sortMenu = await page.evaluate(() => {
          const menu = document.querySelector('[role="menu"], [role="listbox"]');
          if (!menu) return null;
          return { menu: window.__measure(menu), role: menu.getAttribute('role'), items: Array.from(menu.querySelectorAll('[role^="menuitem"], [role="option"]')).map((i) => ({ text: i.innerText.trim().replace(/\s+/g, ' '), role: i.getAttribute('role'), checked: i.getAttribute('aria-checked') || i.getAttribute('aria-selected'), h: i.getBoundingClientRect().height })), html: menu.outerHTML.replace(/<path d="[^"]*"/g, '<path d="…"').slice(0, 5000) };
        });
        await shot(page, 'sort-menu-1280-light.png', { clip: { x: 500, y: 60, width: 520, height: 520 } });
        await page.keyboard.press('Escape'); await sleep(400);
      } catch (e) { r.errors.push('sort ' + e.message); }
      // Column options menu.
      try {
        await page.locator('button[aria-label="Open column options"]').filter({ visible: true }).first().click(); await sleep(700);
        r.columnMenu = await page.evaluate(() => {
          const menu = Array.from(document.querySelectorAll('[role="menu"]')).find((e) => e.getBoundingClientRect().width > 0);
          if (!menu) return null;
          return { menu: window.__measure(menu), items: Array.from(menu.querySelectorAll('[role^="menuitem"]')).map((i) => ({ text: i.innerText.trim().replace(/\s+/g, ' '), role: i.getAttribute('role'), checked: i.getAttribute('aria-checked') })), html: menu.outerHTML.replace(/<path d="[^"]*"/g, '<path d="…"').slice(0, 5000) };
        });
        await shot(page, 'column-options-menu-1280-light.png', { clip: { x: 500, y: 60, width: 520, height: 420 } });
        await page.keyboard.press('Escape'); await sleep(400);
      } catch (e) { r.errors.push('colmenu ' + e.message); }
      // "More" in the type list.
      try {
        await page.locator('[data-testid="kind-group"] button').filter({ hasText: 'More' }).first().click(); await sleep(600);
        r.moreTypes = await page.evaluate(() => Array.from(document.querySelectorAll('[data-testid="kind-group"] [data-component="ActionList.Item"]')).map((li) => (li.innerText || '').trim().replace(/\s+/g, ' ')));
        await shot(page, 'sidebar-more-types-1280-light.png', { clip: { x: 0, y: 72, width: 320, height: 560 } });
      } catch (e) { r.errors.push('more ' + e.message); }
      // "More languages..."
      try {
        await page.locator('[data-testid="facets-pane"] button').filter({ hasText: 'More languages' }).first().click(); await sleep(900);
        r.moreLanguages = await page.evaluate(() => {
          const d = Array.from(document.querySelectorAll('[role="dialog"]')).find((e) => e.getBoundingClientRect().width > 0);
          return d ? { dialog: window.__measure(d), html: d.outerHTML.replace(/<path d="[^"]*"/g, '<path d="…"').slice(0, 4000) } : { facetTexts: Array.from(document.querySelectorAll('[data-testid="facets-pane"] [data-component="ActionList.Item"]')).map((li) => (li.innerText || '').trim()) };
        });
        await shot(page, 'more-languages-1280-light.png');
        await page.keyboard.press('Escape'); await sleep(500);
      } catch (e) { r.errors.push('morelang ' + e.message); }
      // Advanced: "Owner".
      try {
        await page.locator('[data-testid="facets-pane"] button').filter({ hasText: /^Owner$/ }).first().click(); await sleep(1200);
        r.advancedOwner = await page.evaluate(() => {
          const d = Array.from(document.querySelectorAll('[role="dialog"]')).find((e) => e.getBoundingClientRect().width > 0);
          const inp = d ? d.querySelector('input') : document.activeElement;
          return { dialogOpen: !!d, dialog: d && window.__measure(d), inputValue: inp && inp.value, active: document.activeElement && (document.activeElement.getAttribute('aria-label') || document.activeElement.tagName), selStart: inp && inp.selectionStart, overlay: d && (d.querySelector('[data-testid="quick-search-input-overlay"]') || {}).innerHTML, url: location.href };
        });
        await shot(page, 'advanced-owner-click-1280-light.png');
        await page.keyboard.press('Escape'); await sleep(500);
      } catch (e) { r.errors.push('owner ' + e.message); }
      try {
        await page.locator('[data-testid="facets-pane"] button').filter({ hasText: /^Number of stars$/ }).first().click(); await sleep(1000);
        r.advancedStars = await page.evaluate(() => { const d = Array.from(document.querySelectorAll('[role="dialog"]')).find((e) => e.getBoundingClientRect().width > 0); const inp = d ? d.querySelector('input') : null; return { inputValue: inp && inp.value, selStart: inp && inp.selectionStart, overlay: d && (d.querySelector('[data-testid="quick-search-input-overlay"]') || {}).innerHTML }; });
        await shot(page, 'advanced-stars-click-1280-light.png', { clip: { x: 540, y: 0, width: 560, height: 300 } });
        await page.keyboard.press('Escape'); await sleep(500);
        await page.locator('[data-testid="facets-pane"] button').filter({ hasText: /^Date created$/ }).first().click(); await sleep(1000);
        r.advancedCreated = await page.evaluate(() => { const d = Array.from(document.querySelectorAll('[role="dialog"]')).find((e) => e.getBoundingClientRect().width > 0); const inp = d ? d.querySelector('input') : null; return { inputValue: inp && inp.value, selStart: inp && inp.selectionStart }; });
        await page.keyboard.press('Escape'); await sleep(500);
      } catch (e) { r.errors.push('stars ' + e.message); }
      // Hover a result title and a nav item.
      try {
        await page.hover('[data-testid="results-list"] .search-title a'); await sleep(300);
        r.titleHover = await page.evaluate(() => window.__measure(document.querySelector('[data-testid="results-list"] .search-title a')));
        await page.hover('[data-testid="nav-item-issues"]'); await sleep(300);
        r.navHover = await page.evaluate(() => { const a = document.querySelector('[data-testid="nav-item-issues"]'); return { a: window.__measure(a), li: window.__measure(a.closest('li')) }; });
      } catch (e) { r.errors.push('hover ' + e.message); }
      // Dark.
      await page.mouse.move(1270, 890);
      await page.emulateMedia({ colorScheme: 'dark' }); await sleep(900);
      await page.evaluate(() => window.scrollTo(0, 0));
      r.dark = await page.evaluate(MEASURE_RESULTS);
      await shot(page, 'results-repos-1280-dark.png');
      await page.emulateMedia({ colorScheme: 'light' }); await sleep(500);
      // Pagination close-up.
      try {
        await page.locator('nav[aria-label="Pagination"]').scrollIntoViewIfNeeded(); await sleep(400);
        const b = await page.locator('nav[aria-label="Pagination"]').boundingBox();
        await shot(page, 'pagination-1280-light.png', { clip: { x: Math.max(0, b.x - 20), y: Math.max(0, b.y - 20), width: Math.min(1280 - b.x + 20, b.width + 40), height: b.height + 40 } });
      } catch (e) { r.errors.push('pag ' + e.message); }
    } else if (w <= 768) {
      try {
        await page.click('[data-testid="filter-button"]'); await sleep(1000);
        r.filterDrawer = await page.evaluate(() => {
          const d = Array.from(document.querySelectorAll('[role="dialog"]')).find((e) => e.getBoundingClientRect().width > 0);
          if (!d) return null;
          const r = window.__measure(d);
          r.anim = getComputedStyle(d).animationName + ' ' + getComputedStyle(d).animationDuration;
          r.backdrop = window.__hex(getComputedStyle(d.parentElement).backgroundColor);
          r.title = (d.querySelector('h1, h2') || {}).textContent;
          r.attrs = Array.from(d.attributes).filter((a) => a.name.startsWith('data-') || a.name.startsWith('aria')).map((a) => a.name + '=' + a.value);
          r.texts = Array.from(d.querySelectorAll('[data-component="ActionList.Item"], [class*="GroupHeading-"]')).map((e) => (e.innerText || '').trim().replace(/\s+/g, ' ')).slice(0, 60);
          r.html = d.outerHTML.replace(/<path d="[^"]*"/g, '<path d="…"').slice(0, 3000);
          return r;
        });
        await shot(page, `filter-drawer-${w}-light.png`);
        await page.keyboard.press('Escape'); await sleep(600);
      } catch (e) { r.errors.push('filter ' + e.message); }
      try {
        await page.click('[data-testid="type-switcher"]'); await sleep(800);
        r.typeSwitcherMenu = await page.evaluate(() => { const mnu = Array.from(document.querySelectorAll('[role="menu"], [role="listbox"], [role="dialog"]')).find((e) => e.getBoundingClientRect().width > 0); return mnu ? { box: window.__measure(mnu), items: Array.from(mnu.querySelectorAll('[role^="menuitem"], [role="option"], li')).map((i) => (i.innerText || '').trim().replace(/\s+/g, ' ')).slice(0, 20) } : null; });
        await shot(page, `type-switcher-menu-${w}-light.png`);
        await page.keyboard.press('Escape'); await sleep(500);
      } catch (e) { r.errors.push('typeswitch ' + e.message); }
      try { await openHamburgerAndSearch(page, w, r); } catch (e) { r.errors.push('ham ' + e.message); }
    }
    all[w] = r;
    console.log('width', w, 'errors', JSON.stringify(r.errors), 'pane', JSON.stringify(r.m.pane && [r.m.pane.x, r.m.pane.w]), 'main', JSON.stringify(r.m.mainColumn && [r.m.mainColumn.x, r.m.mainColumn.w]), 'cols', JSON.stringify((r.m.twoColumnChildren || []).map((c) => c && [c.x, c.w, c.display])));
    await ctx.close();
    await sleep(2000);
  }
  save('probe5-results' + (process.argv[2] ? '-' + process.argv[2].replace(/,/g, '_') : '') + '.json', all);
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
