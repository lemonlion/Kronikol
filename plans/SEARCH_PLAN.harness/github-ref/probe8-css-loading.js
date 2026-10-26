// Probe 8: one load of the repositories results page. Collect the live CSS rules behind the header trigger,
// the quick search dialog and the result list; measure the loading skeleton; the description match weight.
const { launch, newContext, save, installMeasure, sleep, OUT } = require('./lib');
const path = require('path');
const shot = (page, name, opts = {}) => page.screenshot({ path: path.join(OUT, name), ...opts });

const COLLECT = (patterns) => {
  const res = [];
  const re = new RegExp(patterns);
  const walk = (rules, ctx) => {
    for (const r of rules) {
      if (r.cssRules && (r.media || r.conditionText !== undefined || r.name !== undefined) && !r.selectorText) {
        const c = r.media ? '@media ' + r.media.mediaText : (r.conditionText ? '@supports ' + r.conditionText : (r.name ? '@layer ' + r.name : ''));
        if (r.type === 7 || r.constructor.name === 'CSSKeyframesRule') { if (re.test(r.name)) res.push({ sel: '@keyframes ' + r.name, css: r.cssText.slice(0, 800), ctx }); continue; }
        walk(r.cssRules, ctx ? ctx + ' > ' + c : c);
      } else if (r.selectorText && re.test(r.selectorText)) {
        res.push({ sel: r.selectorText, css: r.style.cssText, ctx });
        if (r.cssRules && r.cssRules.length) walk(r.cssRules, (ctx ? ctx + ' > ' : '') + r.selectorText);
      } else if (r.type === 7 && re.test(r.name)) {
        res.push({ sel: '@keyframes ' + r.name, css: r.cssText.slice(0, 800), ctx });
      }
    }
  };
  for (const s of document.styleSheets) {
    let rules;
    try { rules = s.cssRules; } catch (e) { continue; }
    if (rules) walk(rules, '');
  }
  return res;
};

(async () => {
  const R = {};
  const browser = await launch();
  const ctx = await newContext(browser, { width: 1280, height: 900 });
  const page = await ctx.newPage();
  await page.goto('https://github.com/search?q=playwright&type=repositories', { waitUntil: 'load', timeout: 60000 });
  await sleep(3500);
  await installMeasure(page);
  // Description match weight (the description container is the class that starts with "Content__").
  R.descEm = await page.evaluate(() => {
    const d = document.querySelector('[data-testid="results-list"] [class^="Content__"], [data-testid="results-list"] [class*=" Content__"]');
    return { desc: window.__measure(d), em: window.__measure(d && d.querySelector('em')), searchMatch: window.__measure(d && d.querySelector('.search-match')) };
  });
  // Open the quick search once so its CSS is present, then close it.
  await page.mouse.click(5, 300); await page.keyboard.press('/'); await sleep(1200);
  await page.keyboard.press('Escape'); await sleep(500);
  R.rules = await page.evaluate(COLLECT, 'trigger__|label__Axw|kbd__|content__-QV|searchSlot|ctaContainer|Panel__|alignEnd|quickSearch|searchContainer__|inputWrapper__|inputOverlay__|input__Qi|filterValue__|entity__|constant__|suggestionsList__|actionLabel__|footer__Xw|suggestionName__|suggestionIcon__|Result__|resultRow__|resultContent__|title__ILm|Content__agX|topicLabel__|tokenList__|footer__evn|footerItem__|languageCircle__|counterLabel__|filtersTitle__|facetsContainer__|twoColumnOuter__|mainColumn__|headerRow__|controlsWrapper__|paginationSection__|paginationWrapper__|searchMatchText__|header__ZqT|avatar__|starButtonWrapper__|actionsColumn__|mobileOnly__|desktopOnly|filterButtonWrapper|typeSwitcher|dropdownWrapper|columnLeftWrapper|dataResults__|List__XC|FacetOption__|topDivider__|divider__|linkItem__|searchResultsPage__|SplitPageLayout|keleton|Skeleton|LoadingSkeleton|shimmer');
  console.log('rules', R.rules.length);
  // Loading state: throttle, soft-navigate to Issues, sample the skeleton.
  const cdp = await ctx.newCDPSession(page);
  await cdp.send('Network.enable');
  await cdp.send('Network.emulateNetworkConditions', { offline: false, latency: 3000, downloadThroughput: 40 * 1024, uploadThroughput: 40 * 1024 });
  await page.click('[data-testid="nav-item-issues"]');
  await sleep(900);
  await shot(page, 'loading-state-issues-1280-light.png');
  R.loading = await page.evaluate(() => {
    const m = window.__measure;
    const main = document.querySelector('[class*="mainColumn"]') || document.querySelector('[data-component="SplitPageLayout.Content"]');
    const pane = document.querySelector('[data-testid="facets-pane"]') || document.querySelector('[data-component="PageLayout.Pane"]');
    const blocks = (root) => root ? Array.from(root.querySelectorAll('*')).filter((e) => { const cs = getComputedStyle(e); const r = e.getBoundingClientRect(); return r.width > 4 && r.height > 4 && !e.textContent.trim() && cs.backgroundColor !== 'rgba(0, 0, 0, 0)' && e.tagName !== 'svg' && e.tagName !== 'path' && e.tagName !== 'IMG'; }).slice(0, 14).map((e) => { const r = m(e); const cs = getComputedStyle(e); return { tag: r.tag, cls: String(r.cls).slice(0, 90), dc: e.getAttribute('data-component'), x: r.x, y: r.y, w: r.w, h: r.h, bg: r.bg, radius: r.radius, anim: cs.animationName + ' ' + cs.animationDuration + ' ' + cs.animationIterationCount, bgImage: cs.backgroundImage.slice(0, 160), opacity: cs.opacity }; }) : null;
    return {
      url: location.href,
      mainBlocks: blocks(main),
      paneBlocks: blocks(pane),
      mainHtml: main ? main.outerHTML.replace(/<path d="[^"]*"/g, '<path d="…"').slice(0, 4000) : null,
      ariaBusy: Array.from(document.querySelectorAll('[aria-busy="true"]')).map((e) => e.tagName + '.' + String(e.className).slice(0, 60)),
      live: Array.from(document.querySelectorAll('[aria-live]')).map((e) => ({ live: e.getAttribute('aria-live'), text: (e.textContent || '').trim().slice(0, 120) })).filter((x) => x.text),
      progress: Array.from(document.querySelectorAll('.turbo-progress-bar, [role="progressbar"]')).map((e) => ({ cls: String(e.className), box: e.getBoundingClientRect().toJSON(), bg: window.__hex(getComputedStyle(e).backgroundColor), transition: getComputedStyle(e).transition })),
      currentNav: (document.querySelector('[data-testid="kind-group"] [aria-current]') || {}).textContent,
    };
  });
  await cdp.send('Network.emulateNetworkConditions', { offline: false, latency: 0, downloadThroughput: -1, uploadThroughput: -1 });
  await sleep(6000);
  R.afterLoad = await page.evaluate(() => ({ url: location.href, title: document.title, live: Array.from(document.querySelectorAll('[aria-live]')).map((e) => ({ live: e.getAttribute('aria-live'), text: (e.textContent || '').trim().slice(0, 160) })).filter((x) => x.text), h1: (document.querySelector('h1.sr-only') || {}).textContent, h2: (document.querySelector('#search-results-count') || {}).textContent }));
  save('probe8-css-loading.json', R);
  console.log(JSON.stringify({ descEm: R.descEm.em && [R.descEm.em.fontWeight, R.descEm.em.color, R.descEm.em.bg], loading: { url: R.loading.url, main: R.loading.mainBlocks && R.loading.mainBlocks.slice(0, 4), pane: R.loading.paneBlocks && R.loading.paneBlocks.slice(0, 2), busy: R.loading.ariaBusy, live: R.loading.live, progress: R.loading.progress, currentNav: R.loading.currentNav }, afterLoad: R.afterLoad }, null, 1));
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
