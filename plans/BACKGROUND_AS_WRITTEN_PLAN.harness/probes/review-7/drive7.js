// node drive7.js <pages-old> <pages-new> <file.html> <sequences.js> [filter]: sequences on both builds, typed through the field's input event; prints where they differ.
const path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require(path.resolve(__dirname, '../../../../tools/playwright/installed-browser.js'));

const [oldDir, newDir, file, seqFile, filter] = process.argv.slice(2);
const sequences = require(path.resolve(seqFile));

const HELPERS = `
window.__k = {
  scenario(outline) { return [...document.querySelectorAll('details.scenario')].find(s => s.querySelector(':scope > summary').textContent.includes(outline + ' ') || s.querySelector(':scope > summary').textContent.trim().startsWith(outline)); },
  rows(s) { const t = [...s.querySelectorAll('table')].filter(t => t.querySelector('tbody tr[data-row-idx]') && t.style.display !== 'none'); return t.length ? [...t[0].querySelectorAll('tbody tr[data-row-idx]')] : []; },
  panel(s) { return [...s.querySelectorAll('.param-detail-panel')].find(p => p.style.display !== 'none') || s; },
  sec(p) { const out = []; for (const [k, sel] of [['bg', 'details.scenario-background'], ['st', 'details.scenario-steps'], ['fr', 'details.failure-result']]) { const d = p.querySelector(sel); if (d) out.push(k + (d.hasAttribute('data-holds-failure') ? '*' : '') + (d.open ? '+' : '-')); } return out.join(' '); },
  read(outline) {
    const s = this.scenario(outline);
    if (!s) return 'no scenario';
    const ps = [...s.querySelectorAll('.param-detail-panel')];
    const body = ps.length ? ps.map(p => (p.style.display !== 'none' ? '*' : '') + 'r' + p.id.replace(/.*detail-/, '') + '[' + this.sec(p) + ']').join(' ') : '[' + this.sec(s) + ']';
    const active = this.rows(s).filter(r => r.classList.contains('row-active')).map(r => r.getAttribute('data-row-idx')).join();
    const marks = this.rows(s).filter(r => r.classList.contains('row-search-match')).map(r => r.getAttribute('data-row-idx')).join();
    const diag = [...s.querySelectorAll('details.example-diagrams')].map(d => d.open ? '+' : '-').join('');
    const f = s.closest('details.feature');
    return (s.open ? 'OPEN ' : 'shut ') + (f && !f.open ? '(feature shut) ' : '') + body + (active !== '' ? ' active=' + active : '') + (marks ? ' marked=' + marks : '') + (diag ? ' diag=' + diag : '')
      + ' | tracked=' + (typeof _openedBySearch !== 'undefined' ? _openedBySearch.length : '?') + ' bar=' + JSON.stringify(document.getElementById('searchbar').value) + ' addr=' + location.hash + (getComputedStyle(s).display === 'none' || s.style.display === 'none' ? ' HIDDEN' : '');
  }
};`;

async function ready(page) {
    await page.waitForFunction(() => document.querySelector('details.feature') !== null && typeof fc === 'function', null, { polling: 200, timeout: 60000 });
    await page.evaluate(HELPERS);
    await page.waitForTimeout(150);
}

async function run(browser, report, outline, actions) {
    const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    const url = 'file:///' + report.split(path.sep).join('/');
    await page.goto(url);
    await ready(page);
    const read = async () => { if (!page.url().startsWith('file:')) return 'left the page: ' + page.url(); if (!(await page.evaluate(() => !!window.__k))) await ready(page); return page.evaluate(o => window.__k.read(o), outline); };
    const out = ['start: ' + await read()];
    for (const a of actions) {
        if (typeof a === 'number') {
            await page.evaluate(([o, n]) => { const s = window.__k.scenario(o); window.__k.rows(s)[n].click(); }, [outline, a]);
            await page.waitForTimeout(80);
            out.push(`row ${a}: ` + await read());
            continue;
        }
        const [verb, ...rest] = a.split(':');
        const arg = rest.join(':');
        if (verb === 'q') {
            await page.evaluate(v => { const bar = document.getElementById('searchbar'); bar.value = v; bar.dispatchEvent(new Event('input', { bubbles: true })); }, arg);
            await page.waitForTimeout(arg ? 480 : 120);
            out.push(`search "${arg}": ` + await read());
        } else if (verb === 'e') {
            await page.evaluate(v => { const bar = document.getElementById('searchbar'); bar.value = v; bar.dispatchEvent(new Event('input', { bubbles: true })); bar.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true })); }, arg);
            await page.waitForTimeout(150);
            out.push(`entered "${arg}": ` + await read());
        } else if (verb === 's') {
            const ok = await page.evaluate(([o, kind]) => { const s = window.__k.scenario(o); const d = window.__k.panel(s).querySelector('details.' + kind); if (!d) return false; d.querySelector(':scope > summary').click(); return true; }, [outline, arg]);
            await page.waitForTimeout(80);
            out.push(ok ? `reader toggles ${arg}: ` + await read() : `no ${arg}`);
        } else if (verb === 'open') {
            await page.evaluate(o => { const s = window.__k.scenario(o); for (let e = s; e; e = e.parentElement) if (e.tagName === 'DETAILS' && !e.open) e.querySelector(':scope > summary').click(); }, outline);
            await page.waitForTimeout(120);
            out.push('reader opens scenario: ' + await read());
        } else if (verb === 'shut') {
            await page.evaluate(o => { const s = window.__k.scenario(o); if (s.open) s.querySelector(':scope > summary').click(); }, outline);
            await page.waitForTimeout(120);
            out.push('reader shuts scenario: ' + await read());
        } else if (verb === 'read') {
            out.push('read: ' + await read());
        } else if (verb === 'read2') {
            out.push(`read ${arg}: ` + await page.evaluate(o => window.__k.read(o), arg));
        } else if (verb === 'back' || verb === 'forward') {
            await (verb === 'back' ? page.goBack({ timeout: 4000 }) : page.goForward({ timeout: 4000 })).catch(e => out.push(verb + ' error ' + e.message.slice(0, 80)));
            await page.waitForTimeout(500);
            out.push(`${verb}: ` + await read());
        } else if (verb === 'reload') {
            await page.reload();
            await ready(page);
            await page.waitForTimeout(500);
            out.push('reload: ' + await read());
        } else if (verb === 'goto') {
            await page.goto('about:blank');
            await page.goto(url + '#' + arg);
            await ready(page);
            await page.waitForTimeout(500);
            out.push(`load #${arg}: ` + await read());
        } else if (verb === 'clearall') {
            await page.evaluate(() => clear_all_filters());
            await page.waitForTimeout(200);
            out.push('Clear All: ' + await read());
        } else if (verb === 'eval') {
            out.push(`${a}: ` + await page.evaluate(arg));
        }
    }
    await page.close();
    if (errors.length) out.push('PAGE ERRORS: ' + errors.join(' ; '));
    return out;
}

(async () => {
    const browser = await launchInstalled(chromium, {});
    for (const [outline, label, actions, onlyFile] of sequences) {
        if (filter && !label.includes(filter) && !outline.includes(filter)) continue;
        if (onlyFile && onlyFile !== file) continue;
        const a = await run(browser, path.resolve(oldDir, file), outline, actions);
        const b = await run(browser, path.resolve(newDir, file), outline, actions);
        console.log(`\n## ${outline}: ${label} (${file})`);
        for (let i = 0; i < Math.max(a.length, b.length); i++) {
            if (a[i] === b[i]) console.log('   same ' + a[i]);
            else { console.log(' 5.28.2 ' + a[i]); console.log('  patch ' + b[i]); }
        }
    }
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
