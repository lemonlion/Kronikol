// A reader's actions on a generated report, on 5.28.3 (old) and this cut (new), side by side.
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright');
const { launchInstalled } = require('<tree>/tools/playwright/installed-browser.js');
const ROOT = '<review-10>';

const STATE = (s) => {
    const groups = document.querySelectorAll('details.scenario-parameterized');
    const g = groups[s];
    const panels = [...g.querySelectorAll('.param-detail-panel')];
    const p = panels.find((x) => x.style.display !== 'none');
    const open = (d) => (d ? (d.open ? '1' : '0') : '-');
    const comp = (text) => {
        const c = [...(p ? p.querySelectorAll('details.step-collapsible') : [])].find((d) => d.querySelector(':scope > summary').textContent.includes(text));
        return c ? open(c) : 'u';
    };
    const table = [...g.querySelectorAll('table')].find((t) => t.querySelector('tr[data-row-idx]') && t.style.display !== 'none');
    const active = table ? table.querySelector('tr.row-active') : null;
    const marks = table ? [...table.querySelectorAll('tr.row-search-match')].map((r) => r.getAttribute('data-row-idx')).join('') : '';
    const letter = (i) => (i === null || i === undefined ? '?' : String.fromCharCode(97 + Number(i)));
    return [
        'row=' + letter(active ? active.getAttribute('data-row-idx') : null),
        'bg=' + open(p && p.querySelector('details.scenario-background')),
        'st=' + open(p && p.querySelector('details.scenario-steps')),
        'fr=' + open(p && p.querySelector('details.failure-result')),
        'c0=' + comp('the customer pays'),
        'c00=' + comp('the card is read'),
        'mk=' + (marks.split('').map(letter).join('') || '-'),
        'sc=' + (g.open ? 'o' : 'c'),
    ].join(' ');
};

class Reader {
    constructor(page, ver) { this.page = page; this.ver = ver; this.s = 0; this.log = []; this.errors = []; }
    group() { return this.page.locator('details.scenario-parameterized').nth(this.s); }
    panel() { return this.group().locator('.param-detail-panel:visible'); }
    async rec(label) {
        await this.page.waitForTimeout(120);
        const st = await this.page.evaluate(STATE, this.s);
        this.log.push([label, st]);
        return st;
    }
    state() { return this.page.evaluate(STATE, this.s); }
    async row(letter) {
        const i = letter.charCodeAt(0) - 97;
        await this.group().locator(`tr[data-row-idx="${i}"]:visible`).first().click({ position: { x: 5, y: 5 } });
        return this.rec('hand ' + letter);
    }
    async sec(kind) {
        const css = { bg: 'details.scenario-background', st: 'details.scenario-steps', fr: 'details.failure-result' }[kind];
        await this.panel().locator(css + ' > summary').first().click({ position: { x: 40, y: 8 } });
        return this.rec('toggle ' + kind);
    }
    async comp(which) {
        const text = { c0: 'the customer pays', c00: 'the card is read' }[which];
        const summary = this.panel().locator('details.step-collapsible > summary', { hasText: text }).first();
        await summary.click({ position: { x: 60, y: 8 } });
        await this.page.waitForTimeout(150);
        return this.rec('toggle ' + which);
    }
    async openBox() {
        const box = this.page.locator('#filtering-box');
        if (await box.isVisible()) return;
        await this.page.locator('.nav-filters-toggle').click();
        await this.page.waitForFunction(() => !document.getElementById('filtering-box').hidden, null, { polling: 200 });
    }
    async search(word) {
        await this.openBox();
        const field = this.page.locator('#filtering-box #searchbar');
        await field.fill(word);
        await field.press('Enter');
        await this.page.waitForTimeout(450);
        await this.page.locator('#filtering-box .filters-close').click();
        return this.rec(word ? `search "${word}"` : 'empty search');
    }
    async clearAll() {
        await this.page.evaluate(() => clear_all_filters());
        return this.rec('Clear All');
    }
    async back() {
        await this.page.goBack();
        await this.page.waitForTimeout(300);
        return this.rec('Back');
    }
    async forward() {
        await this.page.goForward();
        await this.page.waitForTimeout(300);
        return this.rec('Forward');
    }
    note(text) { this.log.push(['# ' + text, '']); }
}

async function open(browser, ver, shape, hash = '') {
    const context = await browser.newContext({ viewport: { width: 1400, height: 1100 } });
    const page = await context.newPage();
    const r = new Reader(page, ver);
    page.on('pageerror', (e) => r.errors.push(e.message));
    await page.goto('file:///' + ROOT + `/out-${ver}/${shape}/TestRunReport.html` + hash);
    await page.locator('details.feature').first().waitFor({ state: 'attached' });
    const isNew = await page.evaluate(() => typeof selectRowKept !== 'undefined');
    if (isNew !== (ver === 'new')) throw new Error(`report ${ver} is not the build it should be: selectRowKept ${isNew}`);
    if (!hash) {
        for (const noun of ['features', 'scenarios']) {
            const button = page.locator(`button.fold-expand[data-fold='${noun}']`);
            if ((await button.getAttribute('aria-disabled')) !== 'true') await button.click();
        }
    }
    await page.waitForTimeout(150);
    return { r, context };
}

async function run(scenarios, filter) {
    const browser = await launchInstalled(chromium, { headless: true });
    try {
        for (const sc of scenarios) {
            if (filter && !filter.test(sc.title)) continue;
            const logs = {};
            for (const ver of ['old', 'new']) {
                const { r, context } = await open(browser, ver, sc.shape, sc.hash || '');
                try {
                    await sc.run(r);
                } catch (e) {
                    r.log.push(['THREW ' + String(e.message).split('\n')[0], '']);
                }
                logs[ver] = r;
                await context.close();
            }
            console.log('\n=== ' + sc.title + '  [' + sc.shape + ']');
            const n = Math.max(logs.old.log.length, logs.new.log.length);
            for (let i = 0; i < n; i++) {
                const o = logs.old.log[i] || ['', ''], w = logs.new.log[i] || ['', ''];
                const same = o[1] === w[1];
                console.log((o[0] || w[0]).padEnd(24) + ' 5.28.3 ' + o[1] + (same ? '' : '\n' + ''.padEnd(24) + ' CUT    ' + w[1]));
            }
            for (const ver of ['old', 'new']) if (logs[ver].errors.length) console.log(ver + ' PAGE ERRORS: ' + logs[ver].errors.join(' | '));
        }
    } finally {
        await browser.close();
    }
}

module.exports = { run, open, ROOT, chromium, launchInstalled };
