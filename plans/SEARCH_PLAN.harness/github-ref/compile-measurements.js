// Compile the probe outputs into github-ref/measurements.json.
const fs = require('fs');
const path = require('path');
const J = (f) => JSON.parse(fs.readFileSync(path.join(__dirname, f), 'utf8'));
const p4 = J('probe4-header-qs.json');
const p5 = J('probe5-results.json');
const p6 = J('probe6-types.json');
const p7a = J('probe7-wayback-qb-20260301.json');
const p8 = J('probe8-css-loading.json');
const p9 = J('probe9-1440.json');
const p10 = J('probe10-wayback-results.json');
const tok = J('tokens-from-github-css.json');

const box = (m) => (m ? { x: m.x, y: m.y, w: m.w, h: m.h } : null);
const sty = (m, extra = []) => {
  if (!m) return null;
  const o = { w: m.w, h: m.h, fontSize: m.fontSize, fontWeight: m.fontWeight, lineHeight: m.lineHeight, color: m.color, bg: m.bg, border: m.border, radius: m.radius, padding: m.padding };
  for (const k of extra) o[k] = m[k];
  return o;
};

const header = {};
for (const w of ['1280', '1012', '768', '390']) {
  const h = p4.widths[w].header;
  const r5 = p5[w];
  header[w] = {
    headerHeight: h.header.h,
    headerBg: h.header.bg,
    searchTrigger: h.searchButton ? { ...box(h.searchButton), radius: h.searchButton.radius, border: h.searchButton.border, bg: h.searchButton.bg, color: h.searchButton.color, padding: h.searchButton.padding, content: 'magnifier octicon only (label "Search" and kbd "/" display:none)', ariaLabel: h.searchButtonAria.label, ariaHaspopup: h.searchButtonAria.haspopup } : (r5 && r5.searchInMenu ? { inHamburgerMenu: true, ...box(r5.searchInMenu), radius: r5.searchInMenu.radius, border: r5.searchInMenu.border, bg: r5.searchInMenu.bg, label: { text: r5.searchInMenu.label.text, fontSize: r5.searchInMenu.label.fontSize, color: r5.searchInMenu.label.color }, kbd: { text: '/', w: r5.searchInMenu.kbd.w, h: r5.searchInMenu.kbd.h, fontSize: r5.searchInMenu.kbd.fontSize, fontFamily: r5.searchInMenu.kbd.fontFamily, radius: r5.searchInMenu.kbd.radius, border: r5.searchInMenu.kbd.border, padding: r5.searchInMenu.kbd.padding, boxShadow: r5.searchInMenu.kbd.boxShadow }, menuButton: 'Toggle navigation (hamburger, left)' } : null),
    otherControlsLeftToRight: h.controls.map((c) => `${c.label || '(icon: Appearance settings)'} @x${c.x} ${c.w}x${c.h}`),
  };
}
header['1440'] = {
  headerHeight: 72,
  searchTrigger: { ...box(p9.trigger.button), radius: p9.trigger.button.radius, border: p9.trigger.button.border, bg: p9.trigger.button.bg, padding: p9.trigger.button.padding, icon: 'magnifier display:none at >=1400px', label: { text: p9.trigger.label.text, fontSize: p9.trigger.label.fontSize, fontWeight: p9.trigger.label.fontWeight, color: p9.trigger.label.color }, kbd: { text: '/', w: p9.trigger.kbd.w, h: p9.trigger.kbd.h, fontSize: p9.trigger.kbd.fontSize, fontFamily: p9.trigger.kbd.fontFamily, radius: p9.trigger.kbd.radius, border: p9.trigger.kbd.border, padding: p9.trigger.kbd.padding, boxShadow: p9.trigger.kbd.boxShadow, margin: p9.trigger.kbd.margin } },
};
header.dark1280 = { headerBg: p4.dark.header.header.bg, trigger: { bg: p4.dark.header.searchButton.bg, border: p4.dark.header.searchButton.border, color: p4.dark.header.searchButton.color }, note: 'The logged-out marketing header is black (#000000) in both schemes.' };

const qs = {};
for (const w of ['1280', '1012']) {
  const d = p4.widths[w].dialogEmpty;
  qs[w] = { dialog: box(d.dialog), anchorVars: d.dialogVars, triggerBox: box(p4.widths[w].header.searchButton) };
}
for (const w of ['768', '390']) {
  const d = p5[w].dialogNarrow;
  qs[w] = { dialog: box(d), radius: d.radius, anchorVars: d.vars, input: d.input, note: 'top sheet: inset 0 0 auto, no top radius, max-height 80dvh (CSS @media (width <= 1011px))' };
}
qs['1440'] = { dialog: box(p9.dialog.box), anchorVars: p9.dialog.vars, triggerBox: box(p9.trigger.button) };
const d1280 = p4.widths['1280'].dialogEmpty;
const opt0 = d1280.options[0];
const quickSearch = {
  what: 'Logged-out "Quick search" dialog (React, Primer Dialog), live on github.com 2026-10-26',
  byWidth: qs,
  light: {
    dialog: { radius: d1280.dialog.radius, bg: d1280.dialog.bg, boxShadow: d1280.dialog.boxShadow, animation: d1280.dialogAnim, dataAttrs: d1280.dataAttrs },
    backdrop: d1280.backdrop,
    searchContainerPadding: d1280.searchContainer.padding,
    inputWrapper: sty(d1280.inputWrapper, ['outline', 'boxShadow']),
    input: { fontSize: d1280.input.fontSize, lineHeight: d1280.input.lineHeight, textColor: d1280.input.color + ' (transparent: the overlay draws the text)', placeholder: d1280.inputPlaceholderColor, aria: d1280.inputAria },
    leadingIcon: { w: 16, color: d1280.leadingIcon.color, marginRight: '8px' },
    overlay: { box: box(d1280.overlay), css: 'left: 1+8+16+8 = 33px; right: 1+8+4+24+4 = 41px; pointer-events:none; white-space:pre; font-size 14px' },
    overlayTokens: { qualifierKey: { color: '#1f2328', note: 'plain text, no class' }, filterValue: { color: '#0969da', bg: '#ddf4ff', radius: '3px' }, operatorNOT_OR_minus: { color: '#6639ba' }, regex: { color: '#0969da' }, quotedString: { color: '#1f2328', note: 'plain' } },
    list: { padding: d1280.list.padding, aria: d1280.listAria },
    groupHeading: sty(d1280.groupHeadings[0]),
    option: { li: { h: opt0.li.h, margin: opt0.li.margin, radius: opt0.li.radius }, content: { padding: opt0.padding, display: opt0.display }, leadingIcon: { w: 16, color: opt0.leading.color, marginRight: opt0.leading.margin }, label: { fontSize: opt0.label.fontSize, color: opt0.label.color }, trailing: { text: opt0.trailing.text, fontSize: opt0.trailing.fontSize, color: opt0.trailing.color, shownOn: 'every item, not only the active one' } },
    activeOption: { liBg: p4.keyboard.activeItemStyle.activeLi.bg, marker: '::after 4px wide, height 100%-8px, left -8px, top 4px, radius 6px, #0969da (Primer ActionList [data-is-active-descendant])', label: 'semibold in screenshot qb-empty-active-item-1280-light.png', ariaSelected: 'true' },
    footer: sty(d1280.footer),
    footerLink: { text: 'Search syntax tips', fontSize: d1280.footerLinks[0].fontSize, color: d1280.footerLinks[0].color, href: 'https://docs.github.com/en/search-github/github-code-search/understanding-github-code-search-syntax' },
  },
  dark: (() => { const d = p4.dark.dialogEmpty; return { dialog: { bg: d.dialog.bg, boxShadow: d.dialog.boxShadow }, backdrop: d.backdrop.bg, inputWrapper: { bg: d.inputWrapper.bg, border: d.inputWrapper.border, outline: d.inputWrapper.outline }, placeholder: d.inputPlaceholderColor, groupHeading: d.groupHeadings[0].color, label: d.options[0].label.color, trailing: d.options[0].trailing.color, footerBorder: d.footer.border, footerLink: d.footerLinks[0].color, overlayTokens: p4.dark.typedOverlay.filter((s) => s.cls).map((s) => ({ text: s.text, cls: s.cls, color: s.color, bg: s.bg })) }; })(),
  keyboard: { slashOpens: p4.keyboard.slashOpens, sOpens: p4.keyboard.sOpens, arrowSequence: p4.keyboard.emptyArrows.map((s) => `${s.key} -> ${s.activedescendant}`), focusStaysInInput: true, homeEndMoveCaretNotSelection: true, noWrapAtTop: true, tabOrderFromInput: p4.keyboard.tabOrderFromInput, escapeClosesAndReturnsFocusTo: p4.widths['1280'].afterEscFocus, enterSubmits: p9.afterEnter.url },
  typedStates: Object.fromEntries(Object.entries(p4.typed).filter(([k]) => k !== 'clear').map(([k, v]) => [k, { suggestions: v.optionTexts, ariaExpanded: (v.inputAria || []).find((a) => a.startsWith('aria-expanded')), overlayHtml: v.overlayHtml, dialogHeight: v.dialogH }])),
  emptyState: { group: 'Explore', items: d1280.options.map((o) => o.text.replace(/Explore .*? Jump to/, '').trim() + ' | Jump to'), dialogHeight: d1280.dialog.h },
  liveRegion: 'none found with text in the logged-out quick search',
};

const qa = p7a;
const queryBuilderArchived = {
  what: 'Catalyst <qbsearch-input>/<query-builder> (the component of the logged-in header), rendered from the Wayback Machine copy of github.com/microsoft/playwright captured 2026-03-01; no github.com requests',
  headerButton1280: { ...box(qa.button.button), radius: qa.button.button.radius, container: { ...box(qa.button.container), bg: qa.button.container.bg, border: qa.button.container.border, radius: qa.button.container.radius } },
  container: { ...box(qa.empty.container), radius: qa.empty.container.radius, border: qa.empty.container.border, bg: qa.empty.container.bg, boxShadow: qa.empty.container.boxShadow, padding: qa.empty.container.padding },
  backdrop: qa.empty.backdrop && { bg: qa.empty.backdrop.bg },
  styledInput: { ...box(qa.empty.styledInput), radius: qa.empty.styledInput.radius, margin: qa.empty.styledInput.margin },
  input: { textColor: qa.empty.inputColor, caret: qa.empty.caret, aria: qa.empty.inputAria },
  liveRegionExamples: Object.fromEntries(Object.entries(qa.typed).map(([k, v]) => [k, (v.live || []).map((l) => l.text).join(' ')])),
  typedStates: Object.fromEntries(Object.entries(qa.typed).map(([k, v]) => [k, { items: (v.items || []).slice(1), styled: v.styledContentHtml, containerH: v.containerH }])),
  overlayTokensLight: { 'qb-filter-value': { color: '#0969da', bg: '#ddf4ff', radius: '3px' }, 'qb-entity': { color: '#6639ba' }, 'qb-constant': { color: '#0550ae' } },
  overlayTokensDark: { 'qb-filter-value': { color: '#4493f8', bg: '#388bfd1a' }, 'qb-entity': { color: '#d2a8ff' }, container: { bg: qa.dark.container.bg, border: qa.dark.container.border, boxShadow: qa.dark.container.boxShadow }, backdrop: qa.dark.backdrop && qa.dark.backdrop.bg },
  activeItem: { bg: '#818b9826', marker: '::after 4x24px at left -4px, borderColor-accent-emphasis, radius 6px (primer css .ActionListItem[aria-selected=true]:after)' },
  advancedFacetOwnerClick_20250628: p10.afterOwner && { inputValue: p10.afterOwner.value, caret: p10.afterOwner.caret, focused: p10.afterOwner.focused, opened: p10.afterOwner.open, suggestions: p10.afterOwner.items },
  resultsPageHeaderShowsQuery_20250628: { qbsearchInitialValue: 'react', buttonTextHtml: '<span>react</span>', searchBox1280: { ...box(p10.header.container), bg: p10.header.container.bg, border: p10.header.container.border, radius: p10.header.container.radius } },
};

const results = { layoutByWidth: {}, light1280: {}, dark1280: {} };
for (const w of ['1280', '1012', '768', '390']) {
  const m = p5[w].m;
  results.layoutByWidth[w] = {
    headerH: m.header.h,
    sidebarPane: m.pane && m.pane.w ? { x: m.pane.x, w: m.pane.w, divider: m.verticalDivider && m.verticalDivider.w ? '1px ' + m.verticalDivider.bg : 'none' } : 'hidden (Filter button + bottom-sheet dialog)',
    contentPadding: m.twoColumnOuter.padding,
    mainColumn: { x: m.mainColumn.x, w: m.mainColumn.w },
    rightColumn: m.twoColumnChildren[1] && m.twoColumnChildren[1].w ? { x: m.twoColumnChildren[1].x, w: m.twoColumnChildren[1].w, margin: m.twoColumnChildren[1].margin } : 'hidden',
    resultCard: { w: m.results[0].item.w, h: m.results[0].item.h, padding: m.results[0].item.padding, border: m.results[0].item.border, radius: m.results[0].item.radius },
    gapBetweenCards: m.gapBetweenResults,
    sortButton: m.sortButton && m.sortButton.w ? box(m.sortButton) : 'hidden',
    filterButton: m.filterButton && m.filterButton.w ? box(m.filterButton) : 'hidden',
    typeSwitcher: m.typeSwitcher && m.typeSwitcher.w ? box(m.typeSwitcher) : 'hidden',
    noHorizontalScroll: m.docScrollWidth === Number(w),
  };
}
results.layoutByWidth['1440'] = { sidebarPane: p9.layout.pane.w, mainColumn: p9.layout.main.w, columns: p9.layout.cols.map((c) => ({ x: c.x, w: c.w })) };
const m = p5['1280'].m;
const dm = p5['1280'].dark;
const nav = (n) => ({ h: n.h, liMargin: n.margin, radius: n.radius, contentPadding: n.content.padding, labelSize: n.label.fontSize, labelWeight: n.label.fontWeight, labelColor: n.label.color, iconColor: n.leading && n.leading.color, liBg: n.bg, marker: n.liAfter, counter: n.counter && { text: n.counter.text, h: n.counter.h, fontSize: n.counter.fontSize, fontWeight: n.counter.fontWeight, color: n.counter.color, bg: n.counter.bg, radius: n.counter.radius, padding: n.counter.padding } });
results.light1280 = {
  body: m.root,
  filtersTitle: sty(m.filtersTitle),
  navItemCurrent: nav(m.navItems[1]),
  navItem: nav(m.navItems[2]),
  navItemHoverBg: p5['1280'].navHover && p5['1280'].navHover.li,
  groupHeading: sty(m.groupHeadings[0]),
  languageItem: { h: m.languageItems[0].li.h, dot: m.languageItems[0].dotStyle, dotBox: box(m.languageItems[0].dot) },
  divider: { h: 1, bg: m.dividers[0].bg, margin: m.dividers[0].margin },
  advancedSearchLink: sty(m.advancedSearchLink),
  facetTexts: m.facetTexts,
  countHeader: m.countTexts.map((c) => ({ text: c.text, fontSize: c.fontSize, fontWeight: c.fontWeight })).slice(2, 4),
  timingNote: '"(154 ms)" is a muted smaller span after the count (see screenshot)',
  sortButton: sty(m.sortButton, ['boxShadow']),
  columnOptionsButton: sty(m.columnOptions[1]),
  repoCard: {
    card: sty(m.results[0].item),
    avatar: { w: 20, h: 20, radius: m.results[0].avatar.radius, boxShadow: m.results[0].avatar.boxShadow },
    titleLink: sty(m.results[0].titleLink, ['textDecoration']),
    titleMatchEm: { fontWeight: m.results[0].titleEm.fontWeight, color: m.results[0].titleEm.color, bg: m.results[0].titleEm.bg },
    descMatchEm: { fontWeight: p8.descEm.em.fontWeight, color: p8.descEm.em.color, bg: p8.descEm.em.bg },
    description: { fontSize: p8.descEm.desc.fontSize, lineHeight: p8.descEm.desc.lineHeight, color: p8.descEm.desc.color },
    topicLabel: sty(m.results[0].topic),
    topicList: { gap: m.results[0].topicList.gap, margin: m.results[0].topicList.margin },
    metaRow: { margin: m.results[0].meta.margin, item: sty(m.results[0].metaItem), languageDot: box(m.results[0].metaDot), separator: '·' },
    starButton: sty(m.results[0].starButton, ['boxShadow']),
  },
  titleHover: p5['1280'].titleHover && { color: p5['1280'].titleHover.color, textDecoration: p5['1280'].titleHover.textDecoration },
  pagination: m.pages.map((pg) => ({ text: pg.text, w: pg.w, h: pg.h, color: pg.color, bg: pg.bg, radius: pg.radius, padding: pg.padding, ariaCurrent: pg.ariaCurrent, ariaLabel: pg.ariaLabel, ariaDisabled: pg.ariaDisabled })),
};
results.dark1280 = {
  body: dm.root.bg, verticalDivider: dm.verticalDivider.bg, text: dm.filtersTitle.color, muted: dm.groupHeadings[0].color,
  navCurrentBg: dm.navItems[1].bg, navMarker: dm.navItems[1].liAfter && dm.navItems[1].liAfter.bg, counterBg: dm.navItems[2].counter.bg,
  cardBorder: dm.results[0].item.border, titleLink: dm.results[0].titleLink.color, titleEm: dm.results[0].titleEm.color,
  topic: { color: dm.results[0].topic.color, bg: dm.results[0].topic.bg }, meta: dm.results[0].metaItem.color,
  button: { bg: dm.sortButton.bg, border: dm.sortButton.border, color: dm.sortButton.color },
  pageCurrent: { bg: dm.pages[1].bg, color: dm.pages[1].color }, pageNext: dm.pages[dm.pages.length - 1].color, pagePrevDisabled: dm.pages[0].color,
};
results.issueCard = p6.issues.items[0].rows.map((r) => ({ text: r.text, tag: r.tag, fs: r.fs, fw: r.fw, color: r.color, w: r.w, h: r.h, radius: r.radius }));
results.issueCardDark = p6.issues.dark[0].rows.map((r) => ({ text: r.text, tag: r.tag, color: r.color }));
results.issueLabel = p9.issueLabels[0] && { text: p9.issueLabels[0].text, fontSize: p9.issueLabels[0].fontSize, fontWeight: p9.issueLabels[0].fontWeight, color: p9.issueLabels[0].color, bg: p9.issueLabels[0].bg, border: p9.issueLabels[0].border, radius: p9.issueLabels[0].radius, padding: p9.issueLabels[0].padding, h: p9.issueLabels[0].h };
results.userCard = p6.users.items[1].rows.map((r) => ({ text: r.text, tag: r.tag, fs: r.fs, fw: r.fw, color: r.color, w: r.w, h: r.h, radius: r.radius }));
results.sortOptions = { repositories: p5['1280'].sortMenu.items.map((i) => i.text), issues: p6.issues.sort, users: p6.users.sort, menuRole: p5['1280'].sortMenu.role, itemRole: 'menuitemradio', itemHeight: 32, menuWidth: p5['1280'].sortMenu.menu.w };
results.columnOptionsMenu = p5['1280'].columnMenu.items.map((i) => i.text);
results.typesUnderMore = p5['1280'].moreTypes;
results.facetsByType = { repositories: m.facetTexts, issues: p6.issues.facets.facetTexts, users: p6.users.facets.facetTexts };
results.urlParams = { sortNewestIssues: p6.issues.sortNewestUrl, page2: p6.page2.url, pageCap: 'pagination shows 1..7 … 100 (10 results per page)', enterFromQuickSearch: p9.afterEnter.url };
results.emptyState = { text: p6.empty.text, withOtherTypes: p6.invalid1.text };
results.emptyStateMeasured = { container: box(p6.empty.census[0].box), rows: p6.empty.census[0].rows.filter((r) => r.w > 0).map((r) => ({ text: r.text, tag: r.tag, fs: r.fs, fw: r.fw, lh: r.lh, color: r.color, w: r.w, h: r.h })) };
results.countLine = { count: '16px / 500 / #1f2328 ("171k results")', timing: '12px / 400 / #59636e ("(154 ms)"; seconds when >= 1000 ms per the JS)', srHeading: 'h2#search-results-count.sr-only tabindex=-1 data-react-autofocus' };
results.invalidQueries = { unbalancedParen: p6.invalid1, badRangeAndDate: p6.invalid2, note: 'non-code search reports no syntax error, only 0 results' };
results.codeSearchLoggedOut = p6.code.text;
results.loading = { progressBar: p8.loading.progress[0], skeletonMain: p8.loading.mainBlocks.slice(0, 4), skeletonPane: p8.loading.paneBlocks.slice(0, 2), ariaBusy: p8.loading.ariaBusy, liveRegion: p8.loading.live, navMarkerMovesImmediately: true };
results.filterDrawer390 = (() => { const f = p5['390'].filterDrawer; return { ...box(f), radius: f.radius, animation: f.anim, backdrop: f.backdrop, title: f.title, attrs: f.attrs, groups: f.texts }; })();
results.typeSwitcherMenu390 = p5['390'].typeSwitcherMenu && { box: box(p5['390'].typeSwitcherMenu.box), items: p5['390'].typeSwitcherMenu.items.slice(1) };

const out = {
  meta: {
    measuredOn: '2026-10-26',
    browser: 'Playwright 1.61 bundled Chromium 149 (headless), deviceScaleFactor 1, logged out, en-US',
    note: 'All "measured" numbers come from getBoundingClientRect/getComputedStyle in that browser. Values marked css: come from GitHub production CSS on github.githubassets.com (fetched 2026-10-26). Archived values come from Wayback Machine captures rendered with live GitHub hosts blocked.',
    githubPageLoads: 'about 20 page loads + their XHRs, spaced seconds apart',
    cssSources: ['https://github.githubassets.com/assets/global-58d1122073b19732.css', 'https://github.githubassets.com/assets/primer-38ae573fa780d76a.css', 'https://github.githubassets.com/assets/primer-react-css.dc074e1b2708fdd8.module.css', 'https://github.githubassets.com/assets/blackbird-search.27cfa19c462af0ec.module.css', 'https://github.githubassets.com/assets/light-3a3d2c708970e4e8.css', 'https://github.githubassets.com/assets/dark-c9dacb4ec19616db.css', 'https://github.githubassets.com/assets/primer-primitives-02e066689466a8cd.css'],
    archives: ['https://web.archive.org/web/20250528143500/https://github.com/microsoft/playwright', 'https://web.archive.org/web/20260301075926/https://github.com/microsoft/playwright', 'https://web.archive.org/web/20250628003312/https://github.com/search?q=react&type=repositories'],
  },
  headerLoggedOut: header,
  loggedInAppHeaderFromCss: {
    source: 'css: global-58d1122073b19732.css',
    bar: 'AppHeader bg var(--bgColor-inset) (light #f6f8fa, dark #010409); inset bottom 1px borderColor-default; globalBar padding 16px, gap 12px => 64px tall bar',
    logo: '32x32, radius full',
    searchRegular: 'flex:auto; min-width 12rem (192px); max-width 24rem (384px); right-aligned (justify-content:flex-end)',
    searchButton: 'border 1px --button-default-borderColor-rest (#d1d9e0 light); padding 5px 31px; transparent bg; placeholder fgColor-muted, line-height 20px; leading magnifier 16x16 at top 8px left 8px, fgColor-muted',
    kbd: '16x16, 12px, border 1px solid fgColor-muted, radius 3px, transparent bg, display inline-grid',
    trailingAction: '24x24 at top 4px right 4px, radius 3px, preceded by a 1px x 16px divider (borderColor-default)',
    breakpoint: 'width <= 1011.98px: the regular box is hidden and the narrow icon button shows; when expanded the search becomes position:absolute; top:0; left:0; width:100% over the bar',
    actions: 'gap 8px; a 1px x 20px divider before the action group; actions and Copilot group hidden at width <= 767.98px',
    iconButtons: '32x32, border 1px button-default-borderColor-rest, radius 6px, fgColor-muted; unread dot 8x8 fgColor-accent at top-right with a 2px ring of the header bg',
    overlay: '.search-suggestions: top -14px; left -14px; width calc(100% + 26px); padding-top 12px; radius 12px; max-height 80vh; z-index 35. .dark-backdrop: overlay-backdrop-bgColor, z-index 32',
  },
  quickSearchLoggedOut: quickSearch,
  queryBuilderArchived,
  resultsPage: results,
  tokens: tok.tokens,
  loggedOutNoOps: {
    advancedFacetClick: 'Clicking Owner / Number of stars / Date created in the Advanced group did nothing visible logged out on 2026-10-26 (no dialog, URL unchanged): the facet code calls appendQuery on a header search input that the 2026 logged-out header no longer has. The same click on the 2025-06-28 archive opened the query builder with "react owner:" and the caret at the end.',
    moreLanguagesClick: 'No visible effect logged out; the code maps it to appendQuery "language:" (js/blackbird-search-2f99523020e26693.js)',
    quickSearchSuggestionsWhenTyping: 'none: list empties and aria-expanded=false for every typed query',
    quickSearchPrefill: 'empty on the results page even with q=playwright; Enter from the issues page went to type=repositories',
  },
  screenshots: fs.readdirSync(__dirname).filter((f) => f.endsWith('.png')).sort(),
};
fs.writeFileSync(path.join(__dirname, 'measurements.json'), JSON.stringify(out, null, 2));
console.log('measurements.json', fs.statSync(path.join(__dirname, 'measurements.json')).size, 'bytes');
