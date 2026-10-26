// Probe 4: logged-out header + "Quick search" dialog, measured at 1280/1012/768/390, light and dark.
// One page load of github.com/search; widths change in place.
const { launch, newContext, save, installMeasure, sleep, OUT } = require('./lib');
const path = require('path');

const WIDTHS = [1280, 1012, 768, 390];
const shot = (page, name, clip) => page.screenshot({ path: path.join(OUT, name), clip });

async function measureHeader(page) {
  return page.evaluate(() => {
    const m = window.__measure;
    const header = document.querySelector('header.HeaderMktg, header');
    const cands = Array.from(document.querySelectorAll('button[aria-label*="Search or jump to"]'));
    const vis = (e) => { const r = e.getBoundingClientRect(); const cs = getComputedStyle(e); return r.width > 0 && r.height > 0 && cs.visibility !== 'hidden' && e.checkVisibility && e.checkVisibility({ checkOpacity: true, checkVisibilityCSS: true }); };
    const btn = cands.find(vis) || null;
    window.__searchBtnIndex = btn ? cands.indexOf(btn) : -1;
    const out = { header: m(header), candidates: cands.map(c => Object.assign(m(c), { visible: vis(c), parentCls: c.parentElement.className })) };
    out.searchButton = m(btn);
    if (btn) {
      out.searchButtonAria = { label: btn.getAttribute('aria-label'), haspopup: btn.getAttribute('aria-haspopup'), expanded: btn.getAttribute('aria-expanded') };
      out.searchIcon = m(btn.querySelector('svg'));
      out.searchLabel = m(btn.querySelector('[class*="label__"]'));
      out.searchKbd = m(btn.querySelector('kbd'));
      out.searchContent = m(btn.querySelector('[class*="content__"]'));
    }
    const all = Array.from(header ? header.querySelectorAll('a, button') : []);
    out.controls = all.filter(e => { const r = e.getBoundingClientRect(); const cs = getComputedStyle(e); return r.width > 0 && r.height > 0 && cs.visibility !== 'hidden'; })
      .map(e => ({ label: (e.getAttribute('aria-label') || e.innerText || '').trim().replace(/\s+/g, ' ').slice(0, 40), x: Math.round(e.getBoundingClientRect().x), y: Math.round(e.getBoundingClientRect().y), w: Math.round(e.getBoundingClientRect().width), h: Math.round(e.getBoundingClientRect().height) }));
    return out;
  });
}

async function measureDialog(page) {
  return page.evaluate(() => {
    const m = window.__measure;
    const dlg = Array.from(document.querySelectorAll('[role="dialog"][data-component="Dialog"]')).find(d => d.getBoundingClientRect().width > 0);
    if (!dlg) return { open: false };
    const cs = getComputedStyle(dlg);
    const parent = dlg.parentElement;
    const pcs = getComputedStyle(parent);
    const input = dlg.querySelector('input[role="combobox"]');
    const list = dlg.querySelector('[role="listbox"]');
    const opts = Array.from(dlg.querySelectorAll('[role="option"]'));
    const live = Array.from(document.querySelectorAll('[aria-live]')).filter(e => (e.textContent || '').trim()).map(e => ({ live: e.getAttribute('aria-live'), text: e.textContent.trim().slice(0, 200) }));
    return {
      open: true,
      dialog: m(dlg),
      dialogAnim: { name: cs.animationName, dur: cs.animationDuration, timing: cs.animationTimingFunction, transform: cs.transform },
      dialogVars: dlg.getAttribute('style'),
      dataAttrs: Array.from(dlg.attributes).filter(a => a.name.startsWith('data-')).map(a => a.name + '=' + a.value),
      backdrop: { cls: parent.className, bg: window.__hex(pcs.backgroundColor), anim: pcs.animationName + ' ' + pcs.animationDuration, pos: pcs.position, inset: [pcs.top, pcs.left, pcs.right, pcs.bottom].join(' '), w: parent.getBoundingClientRect().width, h: parent.getBoundingClientRect().height, backdropFilter: pcs.backdropFilter },
      searchContainer: m(dlg.querySelector('[class*="searchContainer"]')),
      inputWrapper: m(dlg.querySelector('.TextInput-wrapper')),
      input: m(input),
      inputPlaceholderColor: (() => { try { return window.__hex(getComputedStyle(input, '::placeholder').color); } catch (e) { return null; } })(),
      inputAria: input ? Array.from(input.attributes).filter(a => a.name.startsWith('aria') || a.name === 'role').map(a => a.name + '=' + a.value) : null,
      leadingIcon: m(dlg.querySelector('.TextInput-icon')),
      clearButton: m(dlg.querySelector('.TextInput-action button')),
      overlay: m(dlg.querySelector('[data-testid="quick-search-input-overlay"]')),
      overlayHtml: (dlg.querySelector('[data-testid="quick-search-input-overlay"]') || {}).innerHTML,
      overlaySpans: Array.from(dlg.querySelectorAll('[data-testid="quick-search-input-overlay"] span')).map(s => ({ text: s.textContent, cls: s.className, color: window.__hex(getComputedStyle(s).color), bg: window.__hex(getComputedStyle(s).backgroundColor), radius: getComputedStyle(s).borderRadius, weight: getComputedStyle(s).fontWeight })),
      inputTextColor: input ? window.__hex(getComputedStyle(input).color) : null,
      inputCaret: input ? getComputedStyle(input).caretColor : null,
      list: m(list),
      listAria: list ? Array.from(list.attributes).filter(a => a.name.startsWith('aria') || a.name === 'role').map(a => a.name + '=' + a.value) : null,
      groupHeadings: Array.from(dlg.querySelectorAll('[class*="GroupHeading-"]')).map(m),
      options: opts.map(o => {
        const r = m(o);
        r.id = o.id; r.selected = o.getAttribute('aria-selected'); r.href = o.getAttribute('href');
        r.trailing = m(o.querySelector('[data-component="ActionList.TrailingVisual"]'));
        r.leading = m(o.querySelector('[data-component="ActionList.LeadingVisual"]'));
        r.label = m(o.querySelector('[data-component="ActionList.Item.Label"]'));
        r.description = m(o.querySelector('[data-component="ActionList.Description"]'));
        r.li = m(o.closest('li'));
        return r;
      }),
      footer: m(dlg.querySelector('[class*="footer__"]')),
      footerLinks: Array.from(dlg.querySelectorAll('[class*="footer__"] a, [class*="footer__"] button')).map(m),
      live,
      activeEl: document.activeElement ? (document.activeElement.getAttribute('aria-label') || document.activeElement.tagName + '.' + document.activeElement.className).slice(0, 80) : null,
      activeDesc: input ? input.getAttribute('aria-activedescendant') : null,
    };
  });
}

async function openDialog(page, how) {
  if (how === 'slash') {
    await page.mouse.click(5, 300);
    await page.keyboard.press('/');
  } else if (how === 's') {
    await page.mouse.click(5, 300);
    await page.keyboard.press('s');
  } else {
    const idx = await page.evaluate(() => window.__searchBtnIndex);
    await page.locator('button[aria-label*="Search or jump to"]').nth(Math.max(0, idx)).click({ timeout: 5000 });
  }
  await sleep(1200);
}

(async () => {
  const results = { url: 'https://github.com/search?q=playwright&type=repositories', at: new Date().toISOString(), widths: {}, typed: {}, keyboard: {}, dark: {} };
  const browser = await launch();
  const ctx = await newContext(browser, { width: 1280, height: 900 });
  const page = await ctx.newPage();
  const xhr = [];
  page.on('request', (r) => { if (['xhr', 'fetch'].includes(r.resourceType()) && r.url().includes('github.com')) xhr.push(r.method() + ' ' + r.url().slice(0, 160)); });
  await page.goto(results.url, { waitUntil: 'load', timeout: 60000 });
  await sleep(3000);
  await installMeasure(page);

  for (const w of WIDTHS) {
    await page.setViewportSize({ width: w, height: 900 });
    await sleep(900);
    const r = { header: await measureHeader(page) };
    await shot(page, `header-loggedout-${w}-light.png`, { x: 0, y: 0, width: w, height: Math.min(140, Math.ceil((r.header.header || { h: 80 }).h) + 10) });
    // Open with a click at every width (the button may be icon-only at narrow widths).
    const btnVisible = !!r.header.searchButton;
    console.log('width', w, 'candidates', JSON.stringify(r.header.candidates.map(c => [c.x, c.y, c.w, c.h, c.visible, c.parentCls])));
    if (btnVisible) {
      await openDialog(page, 'click');
      r.dialogEmpty = await measureDialog(page);
      await shot(page, `qb-empty-${w}-light.png`);
      await page.keyboard.press('Escape');
      await sleep(700);
      r.afterEscFocus = await page.evaluate(() => document.activeElement ? (document.activeElement.getAttribute('aria-label') || document.activeElement.tagName) : null);
      r.afterEscExpanded = await page.evaluate(() => { const b = document.querySelectorAll('button[aria-label*="Search or jump to"]')[Math.max(0, window.__searchBtnIndex)]; return b ? b.getAttribute('aria-expanded') : null; });
    } else {
      r.note = 'search button not visible';
    }
    results.widths[w] = r;
  }

  // Back to 1280 for the typed states and the keyboard.
  await page.setViewportSize({ width: 1280, height: 900 });
  await sleep(800);
  // Hotkeys.
  await openDialog(page, 'slash');
  results.keyboard.slashOpens = (await measureDialog(page)).open;
  await page.keyboard.press('Escape'); await sleep(600);
  await openDialog(page, 's');
  results.keyboard.sOpens = (await measureDialog(page)).open;
  // Arrow keys in the empty state.
  const ids = [];
  for (const key of ['ArrowDown', 'ArrowDown', 'ArrowUp', 'End', 'Home', 'ArrowUp']) {
    await page.keyboard.press(key); await sleep(250);
    const st = await page.evaluate(() => {
      const input = document.querySelector('[role="dialog"] input[role="combobox"]');
      const ad = input && input.getAttribute('aria-activedescendant');
      const sel = Array.from(document.querySelectorAll('[role="dialog"] [role="option"]')).map(o => o.getAttribute('aria-selected') + ':' + (o.getAttribute('data-is-active-descendant') || '') + ':' + o.id);
      const act = document.activeElement;
      return { key: null, activedescendant: ad, focused: act ? (act.id || act.tagName) : null, options: sel };
    });
    st.key = key; ids.push(st);
  }
  results.keyboard.emptyArrows = ids;
  await page.keyboard.press('Home'); await sleep(150);
  // Active-item styling: move to the 2nd option and measure it.
  await page.keyboard.press('ArrowDown'); await sleep(300);
  results.keyboard.activeItemStyle = await page.evaluate(() => {
    const input = document.querySelector('[role="dialog"] input[role="combobox"]');
    const id = input && input.getAttribute('aria-activedescendant');
    const el = id ? document.getElementById(id) : null;
    const li = el ? el.closest('li') : null;
    const other = Array.from(document.querySelectorAll('[role="dialog"] [role="option"]')).find(o => o.id !== id);
    return { id, active: el && window.__measure(el), activeLi: li && window.__measure(li), activeAttrs: el ? Array.from(el.attributes).map(a => a.name + '=' + a.value).filter(s => !s.startsWith('class')) : null, inactive: other && window.__measure(other) };
  });
  await shot(page, 'qb-empty-active-item-1280-light.png', { x: 560, y: 0, width: 520, height: 320 });
  // Tab order from the input.
  const tabs = [];
  await page.focus('[role="dialog"] input[role="combobox"]');
  for (let i = 0; i < 4; i++) {
    await page.keyboard.press('Tab'); await sleep(200);
    tabs.push(await page.evaluate(() => { const a = document.activeElement; return a ? ((a.getAttribute('aria-label') || a.innerText || a.tagName) + ' [' + a.tagName + ' ' + (a.getAttribute('role') || '') + ']').trim().slice(0, 80) : null; }));
  }
  results.keyboard.tabOrderFromInput = tabs;
  await page.focus('[role="dialog"] input[role="combobox"]');

  // Typed states.
  for (const q of ['lan', 'language:', 'language:py', 'is:', 'repo:', 'playwright', 'repo:microsoft/playwright language:typescript NOT test path:*.ts', 'org:github (lang:go OR lang:rust) "exact phrase" /regex.*/ -is:fork']) {
    await page.fill('[role="dialog"] input[role="combobox"]', '');
    await sleep(200);
    await page.type('[role="dialog"] input[role="combobox"]', q, { delay: 60 });
    await sleep(1500);
    const st = await measureDialog(page);
    results.typed[q] = { optionTexts: (st.options || []).map(o => o.text + (o.trailing ? ' | trailing=' + o.trailing.text : '')), groupHeadings: (st.groupHeadings || []).map(g => g.text), overlayHtml: st.overlayHtml, overlaySpans: st.overlaySpans, inputAria: st.inputAria, live: st.live, dialogH: st.dialog && st.dialog.h, footer: st.footerLinks && st.footerLinks.map(f => f.text) };
    const safe = q.replace(/[^a-z0-9]+/gi, '_').replace(/^_|_$/g, '').slice(0, 30) || 'q';
    await shot(page, `qb-typed-${safe}-1280-light.png`, { x: 540, y: 0, width: 560, height: 420 });
    if (q === 'playwright') {
      // Arrow down on typed suggestions to see the active trailing hint.
      await page.keyboard.press('ArrowDown'); await sleep(300);
      results.typed[q].afterArrowDown = await page.evaluate(() => { const i = document.querySelector('[role="dialog"] input[role="combobox"]'); return i.getAttribute('aria-activedescendant'); });
      await shot(page, `qb-typed-playwright-active-1280-light.png`, { x: 540, y: 0, width: 560, height: 420 });
    }
  }
  // Clear button.
  const cleared = await page.evaluate(() => { const b = document.querySelector('[role="dialog"] .TextInput-action button'); if (!b) return 'no clear button'; b.click(); return 'clicked'; });
  await sleep(400);
  results.typed.clear = { cleared, value: await page.evaluate(() => document.querySelector('[role="dialog"] input[role="combobox"]').value), focus: await page.evaluate(() => document.activeElement && document.activeElement.tagName) };
  await page.keyboard.press('Escape'); await sleep(600);

  // Dark.
  await page.emulateMedia({ colorScheme: 'dark' });
  await sleep(800);
  results.dark.header = await measureHeader(page);
  await shot(page, 'header-loggedout-1280-dark.png', { x: 0, y: 0, width: 1280, height: 80 });
  await openDialog(page, 'click');
  results.dark.dialogEmpty = await measureDialog(page);
  await page.keyboard.press('ArrowDown'); await sleep(300);
  results.dark.activeItem = await page.evaluate(() => { const i = document.querySelector('[role="dialog"] input[role="combobox"]'); const el = document.getElementById(i.getAttribute('aria-activedescendant') || ''); return el && window.__measure(el); });
  await shot(page, 'qb-empty-1280-dark.png');
  await page.type('[role="dialog"] input[role="combobox"]', 'repo:microsoft/playwright language:typescript', { delay: 40 });
  await sleep(1200);
  results.dark.typedOverlay = (await measureDialog(page)).overlaySpans;
  await shot(page, 'qb-typed-qualifiers-1280-dark.png', { x: 540, y: 0, width: 560, height: 420 });
  await page.keyboard.press('Escape');
  results.xhr = xhr;
  save('probe4-header-qs.json', results);
  console.log(JSON.stringify({ widths: Object.fromEntries(Object.entries(results.widths).map(([k, v]) => [k, { btn: v.header.searchButton && [v.header.searchButton.x, v.header.searchButton.y, v.header.searchButton.w, v.header.searchButton.h], dlg: v.dialogEmpty && v.dialogEmpty.dialog && [v.dialogEmpty.dialog.x, v.dialogEmpty.dialog.y, v.dialogEmpty.dialog.w, v.dialogEmpty.dialog.h] }])), typed: Object.fromEntries(Object.entries(results.typed).map(([k, v]) => [k, v.optionTexts])), kb: results.keyboard.tabOrderFromInput, xhr: xhr.length }, null, 1));
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
