// Probe 7: render an archived (Wayback Machine) logged-out repo page from 2026-03-01, whose header still
// carried the Catalyst <qbsearch-input> (the component the logged-in header uses), and measure it.
// No github.com requests: every asset comes from web.archive.org.
const { launch, newContext, save, installMeasure, sleep, OUT } = require('./lib');
const path = require('path');
const TS = process.argv[2] || '20260301075926';
const URL = `https://web.archive.org/web/${TS}if_/https://github.com/microsoft/playwright`;
const shot = (page, name, opts = {}) => page.screenshot({ path: path.join(OUT, name), ...opts });

(async () => {
  const R = { url: URL };
  const browser = await launch();
  const ctx = await newContext(browser, { width: 1280, height: 900 });
  const page = await ctx.newPage();
  // Block anything that would reach github.com live.
  await page.route(/^https:\/\/(github\.com|api\.github\.com|collector\.github\.com|github\.githubassets\.com|avatars\.githubusercontent\.com)\//, (r) => r.abort());
  const failed = [];
  page.on('requestfailed', (r) => failed.push(r.url().slice(0, 140)));
  await page.goto(URL, { waitUntil: 'load', timeout: 120000 });
  await sleep(6000);
  await installMeasure(page);
  R.defined = await page.evaluate(() => ({ qb: !!customElements.get('qbsearch-input'), queryBuilder: !!customElements.get('query-builder'), modal: !!customElements.get('modal-dialog') }));
  console.log('defined', JSON.stringify(R.defined));
  R.button = await page.evaluate(() => {
    const b = document.querySelector('qbsearch-input .header-search-button');
    if (!b) return null;
    const c = document.querySelector('qbsearch-input .search-input-container');
    return { button: window.__measure(b), container: window.__measure(c), qb: window.__measure('qbsearch-input'), icon: window.__measure(b.querySelector('svg')), text: window.__measure(b.querySelector('[data-target="qbsearch-input.inputButtonText"]')), kbd: window.__measure(b.querySelector('[data-target="qbsearch-input.hotkeyIndicator"] svg')), headerControls: Array.from(document.querySelectorAll('header a, header button')).filter((e) => e.getBoundingClientRect().width > 0 && e.checkVisibility()).map((e) => ({ label: (e.getAttribute('aria-label') || e.innerText || '').trim().replace(/\s+/g, ' ').slice(0, 40), x: Math.round(e.getBoundingClientRect().x), y: Math.round(e.getBoundingClientRect().y), w: Math.round(e.getBoundingClientRect().width), h: Math.round(e.getBoundingClientRect().height) })) };
  });
  console.log('button', JSON.stringify(R.button && R.button.button && [R.button.button.x, R.button.button.y, R.button.button.w, R.button.button.h, R.button.button.radius, R.button.button.border, R.button.button.bg]));
  await shot(page, `wayback-${TS.slice(0,8)}-header-qbsearch-1280-light.png`, { clip: { x: 0, y: 0, width: 1280, height: 80 } });
  // Widths: the button at each width.
  R.widths = {};
  for (const w of [1280, 1012, 768, 390]) {
    await page.setViewportSize({ width: w, height: 900 }); await sleep(800);
    R.widths[w] = await page.evaluate(() => {
      const b = document.querySelector('qbsearch-input .header-search-button');
      const vis = (e) => e && e.getBoundingClientRect().width > 0 && e.checkVisibility();
      const alt = Array.from(document.querySelectorAll('header button, header a')).filter((e) => vis(e) && /search/i.test((e.getAttribute('aria-label') || '') + ' ' + (e.innerText || ''))).map((e) => window.__measure(e));
      return { button: vis(b) ? window.__measure(b) : null, text: vis(b) ? window.__measure(b.querySelector('[data-target="qbsearch-input.inputButtonText"]')) : null, otherSearch: alt.map((a) => ({ text: a.text, x: a.x, y: a.y, w: a.w, h: a.h })) };
    });
    await shot(page, `wayback-${TS.slice(0,8)}-header-qbsearch-${w}-light.png`, { clip: { x: 0, y: 0, width: w, height: 80 } });
  }
  await page.setViewportSize({ width: 1280, height: 900 }); await sleep(800);
  // Open with "/".
  await page.mouse.click(640, 600);
  await page.keyboard.press('/');
  await sleep(1500);
  const measureQB = () => page.evaluate(() => {
    const m = window.__measure;
    const qb = document.querySelector('qbsearch-input');
    const cont = qb && qb.querySelector('.query-builder-container');
    const dlg = qb && qb.querySelector('modal-dialog');
    const input = qb && qb.querySelector('input[role="combobox"]');
    const list = qb && qb.querySelector('ul[role="listbox"]');
    const items = list ? Array.from(list.querySelectorAll('li')).filter((li) => li.getBoundingClientRect().height > 0) : [];
    const backdrop = qb && qb.querySelector('.dark-backdrop');
    return {
      containerHidden: cont ? cont.hidden : null,
      container: m(cont), dialog: m(dlg), overlayWrapper: dlg ? m(dlg.parentElement) : null,
      backdrop: backdrop && !backdrop.hidden ? m(backdrop) : null,
      styledInput: m(qb && qb.querySelector('.QueryBuilder-StyledInput')),
      input: m(input), inputAria: input ? Array.from(input.attributes).filter((a) => a.name.startsWith('aria') || a.name === 'role').map((a) => a.name + '=' + a.value) : null,
      styledContentHtml: qb && (qb.querySelector('.QueryBuilder-StyledInputContent') || {}).innerHTML,
      styledSpans: qb ? Array.from(qb.querySelectorAll('.QueryBuilder-StyledInputContent span')).map((s) => ({ text: s.textContent, cls: s.className, color: window.__hex(getComputedStyle(s).color), bg: window.__hex(getComputedStyle(s).backgroundColor), radius: getComputedStyle(s).borderRadius })) : null,
      inputColor: input ? window.__hex(getComputedStyle(input).color) : null,
      caret: input ? getComputedStyle(input).caretColor : null,
      list: m(list),
      items: items.map((li) => { const r = m(li); r.role = li.getAttribute('role'); r.id = li.id; r.ariaSelected = li.getAttribute('aria-selected'); r.html = li.outerHTML.replace(/<path d="[^"]*"/g, '<path d="…"').slice(0, 1500); return r; }),
      headers: list ? Array.from(list.querySelectorAll('[role="presentation"] , .ActionList-sectionDivider, h3, [class*="sectionDivider"]')).map((h) => ({ text: (h.innerText || '').trim(), cls: h.className, fs: getComputedStyle(h).fontSize, fw: getComputedStyle(h).fontWeight, color: window.__hex(getComputedStyle(h).color) })) : null,
      live: Array.from(document.querySelectorAll('qbsearch-input [aria-live]')).map((e) => ({ text: (e.textContent || '').trim(), live: e.getAttribute('aria-live') })),
      footer: m(qb && qb.querySelector('.search-feedback-prompt')),
      footerLinks: qb ? Array.from(qb.querySelectorAll('.search-feedback-prompt a, .search-feedback-prompt button')).map((a) => ({ text: (a.innerText || '').trim(), color: window.__hex(getComputedStyle(a).color), fs: getComputedStyle(a).fontSize })) : null,
      validation: (() => { const v = qb && qb.querySelector('.FormControl-inlineValidation'); return v ? { hidden: v.hidden, text: (v.innerText || '').trim(), m: m(v) } : null; })(),
      activeDesc: input ? input.getAttribute('aria-activedescendant') : null,
    };
  });
  R.empty = await measureQB();
  console.log('empty container', JSON.stringify(R.empty.container && [R.empty.container.x, R.empty.container.y, R.empty.container.w, R.empty.container.h]), 'items', R.empty.items.length);
  await shot(page, 'wayback-' + TS.slice(0,8) + '-qb-empty-1280-light.png');
  R.typed = {};
  const input = 'qbsearch-input input[role="combobox"]';
  for (const q of ['lan', 'language:', 'language:py', 'is:', 'repo:', 'playwright', 'repo:microsoft/playwright language:typescript NOT test path:*.ts', 'org:github (lang:go OR lang:rust) "exact phrase" /regex.*/ -is:fork', 'foo (bar', 'path:/src/ /unclosed']) {
    try {
      await page.fill(input, '');
      await sleep(200);
      await page.type(input, q, { delay: 50 });
      await sleep(1600);
      const st = await measureQB();
      R.typed[q] = { items: st.items.map((i) => i.text), headers: st.headers, styledContentHtml: st.styledContentHtml, styledSpans: st.styledSpans, inputAria: st.inputAria, live: st.live, validation: st.validation, containerH: st.container && st.container.h };
      const safe = q.replace(/[^a-z0-9]+/gi, '_').replace(/^_|_$/g, '').slice(0, 30) || 'q';
      await shot(page, `wayback-${TS.slice(0,8)}-qb-typed-${safe}-1280-light.png`, { clip: { x: 0, y: 0, width: 1280, height: 520 } });
      if (q === 'playwright' || q === 'lan') {
        await page.keyboard.press('ArrowDown'); await sleep(300);
        const a = await measureQB();
        R.typed[q].afterDown = { activeDesc: a.activeDesc, selected: a.items.filter((i) => i.ariaSelected === 'true').map((i) => ({ text: i.text, bg: i.bg, html: i.html })) };
        await shot(page, `wayback-${TS.slice(0,8)}-qb-typed-${safe}-active-1280-light.png`, { clip: { x: 0, y: 0, width: 1280, height: 520 } });
      }
    } catch (e) { R.typed[q] = { err: e.message }; }
  }
  // Dark.
  await page.fill(input, ''); await sleep(300);
  await page.emulateMedia({ colorScheme: 'dark' }); await sleep(800);
  await page.type(input, 'repo:microsoft/playwright language:typescript NOT test', { delay: 30 }); await sleep(1500);
  R.dark = await measureQB();
  await shot(page, 'wayback-' + TS.slice(0,8) + '-qb-typed-qualifiers-1280-dark.png', { clip: { x: 0, y: 0, width: 1280, height: 520 } });
  R.failedCount = failed.length;
  R.failedSample = failed.slice(0, 30);
  save('probe7-wayback-qb-' + TS.slice(0,8) + '.json', R);
  console.log(JSON.stringify({ typed: Object.fromEntries(Object.entries(R.typed).map(([k, v]) => [k, v.items || v.err])) }, null, 1));
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
