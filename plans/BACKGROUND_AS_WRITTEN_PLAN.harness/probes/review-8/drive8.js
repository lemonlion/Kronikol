// node drive8.js <pages-old> <pages-new> <file.html> <sequences.js> [filter]
// Each sequence on both builds, on Playwright's bundled Chromium, printing the row panels' sections after each action. A reader's actions are real clicks
// on the summaries and rows; a search value goes through the field's own input event, then waits out the 300 ms timer.
const path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require(path.resolve(__dirname, '../../../../tools/playwright/installed-browser.js'));

const [oldDir, newDir, file, seqFile, filter] = process.argv.slice(2);
const sequences = require(path.resolve(seqFile));

const HELPERS = `
window.__k = {
  title(s) { return s.querySelector(':scope > summary').textContent; },
  scenario(name) { return [...document.querySelectorAll('details.scenario')].find(s => { const t = this.title(s); return t.includes(name + ' ') || t.trim().endsWith(name) || t.includes(name + '('); }) || [...document.querySelectorAll('details.scenario')].find(s => this.title(s).includes(name)); },
  rows(s) { const t = [...s.querySelectorAll('table')].filter(t => t.querySelector('tbody tr[data-row-idx]') && t.style.display !== 'none'); return t.length ? [...t[0].querySelectorAll('tbody tr[data-row-idx]')] : []; },
  panel(s) { return [...s.querySelectorAll('.param-detail-panel')].find(p => p.style.display !== 'none') || s; },
  sec(p) { const out = []; for (const [k, sel] of [['bg', 'details.scenario-background'], ['st', 'details.scenario-steps'], ['fr', 'details.failure-result']]) { const d = p.querySelector(sel); if (d) out.push(k + (d.open ? '+' : '-') + (d.hasAttribute('data-holds-failure') ? 'h' : '')); } const c = [...p.querySelectorAll('details.step-collapsible')]; if (c.length) out.push('c' + c.map(d => d.open ? '+' : '-').join('')); return out.join(' '); },
  read(name) {
    const s = this.scenario(name);
    if (!s) return 'no scenario ' + name;
    const ps = [...s.querySelectorAll('.param-detail-panel')];
    const body = ps.length ? ps.map(p => (p.style.display !== 'none' ? '*' : '') + 'r' + p.id.replace(/.*detail-/, '') + '[' + this.sec(p) + ']').join(' ') : '[' + this.sec(s) + ']';
    const active = this.rows(s).filter(r => r.classList.contains('row-active')).map(r => r.getAttribute('data-row-idx')).join();
    const diag = [...s.querySelectorAll('details.example-diagrams')].map(d => d.open ? '+' : '-').join('');
    const f = s.closest('details.feature');
    const tracked = (typeof _sectionsOpenedBySearch !== 'undefined' ? _sectionsOpenedBySearch.length : '?');
    const kept = typeof selectRowKept !== 'undefined' ? Object.values(selectRowKept).map(k => JSON.stringify(k.sections).replace(/"/g, '').replace(/scenario-/g, '')).join('') : '';
    return (s.open ? 'OPEN ' : 'shut ') + (f && !f.open ? '(feature shut) ' : '') + (s.style.display === 'none' ? '(hidden) ' : '') + body + (active !== '' ? ' row=' + active : '') + (diag ? ' diag=' + diag : '') + ' | searchSections=' + tracked + ' kept=' + kept;
  }
};`;

async function ready(page) {
    await page.waitForFunction(() => document.querySelector('details.feature') !== null && typeof fc === 'function', null, { polling: 200, timeout: 60000 });
    await page.evaluate(HELPERS);
    await page.waitForTimeout(150);
}

async function run(browser, report, name, actions) {
    const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    await page.goto('file:///' + report.split(path.sep).join('/'));
    await ready(page);
    const read = async n => { if (!(await page.evaluate(() => !!window.__k))) await ready(page); return page.evaluate(o => window.__k.read(o), n || name); };
    const out = ['start: ' + await read()];
    for (const a of actions) {
        if (typeof a === 'number') {
            await page.evaluate(([o, n]) => { const s = window.__k.scenario(o); window.__k.rows(s)[n].click(); }, [name, a]);
            await page.waitForTimeout(80);
            out.push(`reader clicks row ${a}: ` + await read());
            continue;
        }
        const [verb, ...rest] = a.split(':');
        const arg = rest.join(':');
        if (verb === 'q') {
            await page.evaluate(v => { const bar = document.getElementById('searchbar'); bar.value = v; bar.dispatchEvent(new Event('input', { bubbles: true })); }, arg);
            await page.waitForTimeout(arg ? 480 : 120);
            out.push(`search "${arg}": ` + await read());
        } else if (verb === 's') {
            const ok = await page.evaluate(([o, kind]) => { const s = window.__k.scenario(o); const d = window.__k.panel(s).querySelector('details.' + kind); if (!d) return false; d.querySelector(':scope > summary').click(); return true; }, [name, arg]);
            await page.waitForTimeout(80);
            out.push(ok ? `reader toggles ${arg}: ` + await read() : `no ${arg} on the row shown`);
        } else if (verb === 'd') {
            const ok = await page.evaluate(([o, kind]) => { const s = window.__k.scenario(o); const d = s.querySelector('details.' + kind); if (!d) return false; d.querySelector(':scope > summary').click(); return true; }, [name, arg]);
            await page.waitForTimeout(80);
            out.push(ok ? `reader toggles ${arg}: ` + await read() : `no ${arg}`);
        } else if (verb === 'c') {
            const ok = await page.evaluate(([o, n]) => { const s = window.__k.scenario(o); const d = window.__k.panel(s).querySelectorAll('details.step-collapsible')[+n]; if (!d) return false; d.querySelector(':scope > summary').click(); return true; }, [name, arg]);
            await page.waitForTimeout(80);
            out.push(ok ? `reader toggles composite ${arg}: ` + await read() : `no composite ${arg}`);
        } else if (verb === 'open') {
            await page.evaluate(o => { const s = window.__k.scenario(o); for (let e = s; e; e = e.parentElement) if (e.tagName === 'DETAILS' && !e.open) e.querySelector(':scope > summary').click(); }, name);
            await page.waitForTimeout(120);
            out.push('reader opens scenario: ' + await read());
        } else if (verb === 'shut') {
            await page.evaluate(o => { const s = window.__k.scenario(o); if (s.open) s.querySelector(':scope > summary').click(); }, name);
            await page.waitForTimeout(120);
            out.push('reader shuts scenario: ' + await read());
        } else if (verb === 'read') {
            out.push(`read ${arg || name}: ` + await read(arg || name));
        } else if (verb === 'back' || verb === 'forward') {
            await (verb === 'back' ? page.goBack({ timeout: 4000 }) : page.goForward({ timeout: 4000 })).catch(e => out.push(verb + ' error ' + e.message.slice(0, 80)));
            await page.waitForTimeout(500);
            out.push(`${verb}: ` + await read());
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
    for (const [name, label, actions, onlyFile] of sequences) {
        if (filter && !label.includes(filter) && !name.includes(filter)) continue;
        if (onlyFile && onlyFile !== file) continue;
        const a = await run(browser, path.resolve(oldDir, file), name, actions);
        const b = await run(browser, path.resolve(newDir, file), name, actions);
        console.log(`\n## ${name}: ${label} (${file})`);
        for (let i = 0; i < Math.max(a.length, b.length); i++) {
            if (a[i] === b[i]) console.log('   both ' + a[i]);
            else { console.log(' 5.28.2 ' + a[i]); console.log('  patch ' + b[i]); }
        }
    }
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
