// DIAGRAM_COLOURS_PLAN harness, I3: old and new header forms in one merged report.
// Opens a `kronikol merge` output and, on every scenario diagram, drives what reads a note's header lines:
//   ink      the fill painted on the header lines ([Key=value], [Full path])
//   yaml     per note, whether hovering offers the JSON⇄YAML button (reconstructNoteJson)
//   copy     Copy box text on the first note: a colour tag left in the clipboard (noteLinesToText)
//   payload  Copy all caller request payloads: a tag or a header line left in it (extractCallerPayloads)
//   yamlOn   the first eligible note switched to YAML: header keys still painted, YAML body painted
//            (applyNoteFormats, activeNoteContentLines)
//   hide     the scenario's headers toggle: header keys still painted afterwards (buildSourceWithNoteStates),
//            and per note the YAML button again while hidden (the note-to-group alignment)
//   collapse the first note collapsed: header keys still painted (getNotePreview), and a tag in its
//            tooltip (the collapsed-note title)
// Twins: the "old:" and "new:" scenarios are one shape written by two builds, so every column must
// agree between them; the 3.1.0 fixtures must agree between the two pages.
// Usage: node merge-probe.js <merged TestRunReport.html> [label]
// Needs the E2E project built once (its Playwright driver) and network access to the CDN.
const path = require('path'), url = require('url');
const REPO = path.resolve(__dirname, '../..');
const pw = require(REPO + '/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package');
const file = process.argv[2];
const label = process.argv[3] || path.basename(path.dirname(file));
const HEADER = /^\[(?:[A-Za-z][A-Za-z0-9-]*=|Full$)/;   // a painted header token: [Key=… or the [Full of [Full path]

const sleep = ms => new Promise(r => setTimeout(r, ms));

(async () => {
    const browser = await pw.chromium.launch({ headless: true });
    const context = await browser.newContext({ permissions: ['clipboard-read', 'clipboard-write'], viewport: { width: 1600, height: 1200 } });
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', e => errors.push(String(e && e.message || e)));
    await page.goto(url.pathToFileURL(path.resolve(file)).href);
    await page.locator('details.feature').first().waitFor();
    await page.locator('button.collapse-expand-all', { hasText: 'Expand All Features' }).first().click();
    await page.locator('button.collapse-expand-all', { hasText: 'Expand All Scenarios' }).first().click();

    const targets = await page.evaluate(() => {
        const out = [];
        document.querySelectorAll('details.scenario').forEach(s => {
            const c = s.querySelector("[data-diagram-type='plantuml']");
            if (!c) return;
            const i = out.length;
            c.setAttribute('data-i3', String(i));
            const name = (s.querySelector('summary') || s).textContent.replace(/\s+/g, ' ').trim();
            out.push({ i, name });
        });
        if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body);
        return out;
    });

    const sel = i => `[data-i3="${i}"]`;
    // Ready: drawn with its note overlays, or settled without an svg (the engine wrote its failure text into
    // the element instead), which is reported as the row's error rather than waited out.
    const ready = async (i, timeout = 90000) => {
        await page.waitForFunction(i => {
            const c = document.querySelector(`[data-i3="${i}"]`);
            if (!c || c._noteRendering || window._plantumlRendering) return false;
            const svg = c.querySelector('svg');
            if (!svg) return c.dataset.rendered === '1' && c.textContent.trim().length > 0;
            return c.querySelectorAll('.note-hover-rect').length > 0;
        }, i, { polling: 200, timeout });
        const held = await page.evaluate(s => { const c = document.querySelector(s); return c.querySelector('svg') ? '' : c.textContent.trim(); }, sel(i));
        if (held) throw new Error('not drawn: ' + held.slice(0, 90));
    };
    const svgHtml = i => page.evaluate(s => { const v = document.querySelector(s + ' svg'); return v ? v.outerHTML : ''; }, sel(i));
    const rerendered = (i, before) => page.waitForFunction(([s, before]) => {
        const c = document.querySelector(s);
        if (!c || c._noteRendering || window._plantumlRendering) return false;
        const v = c.querySelector('svg');
        return !!v && v.outerHTML !== before && c.querySelectorAll('.note-hover-rect').length > 0;
    }, [sel(i), before], { polling: 200, timeout: 30000 });
    const headerKeys = i => page.evaluate(([s, re]) => {
        const rx = new RegExp(re);
        const keys = new Set();
        document.querySelectorAll(s + ' svg text').forEach(t => {
            const x = t.textContent.trim();
            if (rx.test(x)) keys.add(x === '[Full' ? 'Full path' : x.slice(1, x.indexOf('=')));
        });
        return [...keys].sort();
    }, [sel(i), HEADER.source]);
    const painted = i => page.evaluate(s => Array.from(document.querySelectorAll(s + ' svg text')).map(t => t.textContent).join(' '), sel(i));
    const unhover = async i => {
        await page.evaluate(s => document.querySelectorAll(s + ' .note-hover-rect').forEach(r => r.dispatchEvent(new MouseEvent('mouseleave', { bubbles: true }))), sel(i));
        await page.waitForFunction(s => !Array.from(document.querySelectorAll(s + ' .note-toggle-icon')).some(b => b.style.opacity === '1'),
            sel(i), { polling: 200, timeout: 5000 }).catch(() => {});
    };
    const hover = (i, k) => page.evaluate(([s, k]) => {
        const r = document.querySelectorAll(s + ' .note-hover-rect')[k];
        if (r) r.dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }));
        return !!r;
    }, [sel(i), k]);
    const yamlVector = async i => {
        const n = await page.evaluate(s => document.querySelectorAll(s + ' .note-hover-rect').length, sel(i));
        let v = '';
        for (let k = 0; k < n; k++) {
            await unhover(i);
            await hover(i, k);
            await sleep(250);
            const shown = await page.evaluate(s => Array.from(document.querySelectorAll(s + " [data-note-btn='format']"))
                .some(b => b.style.display !== 'none' && b.style.opacity === '1'), sel(i));
            v += shown ? 'Y' : '.';
        }
        await unhover(i);
        return v;
    };
    const clip = () => page.evaluate(() => navigator.clipboard.readText());
    const openMenu = async i => {
        await page.evaluate(() => navigator.clipboard.writeText(''));
        await page.evaluate(s => {
            const el = document.querySelector(s + ' .note-hover-rect');
            const r = el.getBoundingClientRect();
            el.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true,
                clientX: r.left + r.width / 2, clientY: r.top + r.height / 2 }));
        }, sel(i));
        await page.locator('.diagram-ctx-menu').first().waitFor({ timeout: 5000 });
    };
    const closeMenu = () => page.evaluate(() => document.querySelectorAll('.diagram-ctx-menu').forEach(m => m.remove()));
    const tagIn = t => /<color:/.test(t) ? 'TAG' : '-';
    const headerLineIn = t => t.split('\n').some(l => /^\s*\[[A-Za-z][A-Za-z0-9-]*=/.test(l)) ? 'HDR' : '-';

    const rows = [];
    for (const t of targets) {
        const row = { page: label, name: t.name.slice(0, 58) };
        Object.defineProperty(row, 'full', { value: t.name, enumerable: false });
        try {
            await page.locator(sel(t.i)).scrollIntoViewIfNeeded();
            await ready(t.i);
            const src = await page.evaluate(s => document.querySelector(s).getAttribute('data-plantuml') || '', sel(t.i));
            const gray = (src.match(/<color:gray>/g) || []).length, hex = (src.match(/<color:#[0-9A-Fa-f]{6}>/g) || []).length;
            row.form = gray && hex ? 'mixed' : gray ? 'gray' : hex ? 'hex' : 'none';
            row.ink = (await page.evaluate(([s, re]) => {
                const rx = new RegExp(re), fills = new Set();
                document.querySelectorAll(s + ' svg text').forEach(x => { if (rx.test(x.textContent.trim())) fills.add(x.getAttribute('fill')); });
                return [...fills].join(',');
            }, [sel(t.i), HEADER.source])) || '-';
            row.keys = (await headerKeys(t.i)).join('|') || '-';
            row.yaml = await yamlVector(t.i);

            await openMenu(t.i);
            const copyItem = page.locator('.diagram-ctx-menu').getByText('Copy box text', { exact: true }).first();
            const isParent = await copyItem.evaluate(el => !!el.closest('.submenu-parent'));
            if (isParent) {
                await page.locator('.diagram-ctx-menu .submenu-parent', { hasText: 'Copy box text' }).first().hover();
                await page.locator('.diagram-ctx-menu .submenu').getByText('Copy full box text').first().click();
            } else await copyItem.click();
            await sleep(150);
            const c1 = await clip();
            row.copy = tagIn(c1) + '/' + headerLineIn(c1);
            await closeMenu();

            await openMenu(t.i);
            const payloadItem = page.locator('.diagram-ctx-menu').getByText('Copy all caller request payloads', { exact: true });
            if (await payloadItem.count()) {
                await payloadItem.first().click();
                await sleep(150);
                const c2 = await clip();
                row.payload = tagIn(c2) + '/' + headerLineIn(c2);
            } else row.payload = 'none';
            await closeMenu();

            // yamlOn: the first eligible note in YAML
            const first = row.yaml.indexOf('Y');
            if (first >= 0) {
                const before = await svgHtml(t.i);
                await hover(t.i, first);
                await sleep(250);
                await page.evaluate(s => {
                    const b = Array.from(document.querySelectorAll(s + " [data-note-btn='format']")).find(b => b.style.display !== 'none' && b.style.opacity === '1');
                    b.querySelector('rect').dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
                }, sel(t.i));
                await rerendered(t.i, before);
                const text = await painted(t.i);
                row.yamlOn = (await headerKeys(t.i)).join('|') === row.keys && /\w+: /.test(text) ? 'ok' : 'keys:' + (await headerKeys(t.i)).join('|');
                const back = await svgHtml(t.i);
                await hover(t.i, first);
                await sleep(250);
                await page.evaluate(s => {
                    const b = Array.from(document.querySelectorAll(s + " [data-note-btn='format']")).find(b => b.style.display !== 'none' && b.style.opacity === '1');
                    if (b) b.querySelector('rect').dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
                }, sel(t.i));
                await rerendered(t.i, back).catch(() => {});
                await unhover(t.i);
            } else row.yamlOn = 'n/a';

            // hide: the scenario's own headers toggle
            const toggle = page.locator(`details.scenario:has(${sel(t.i)}) .toggle-btn[data-toggle='headers'][data-shown='true']`).first();
            if (await toggle.count()) {
                const before = await svgHtml(t.i);
                await toggle.click();
                await rerendered(t.i, before).catch(() => {});
                row.hide = (await headerKeys(t.i)).join('|') || 'none';
                row.yamlHidden = await yamlVector(t.i);
                const back = await svgHtml(t.i);
                await page.locator(`details.scenario:has(${sel(t.i)}) .toggle-btn[data-toggle='headers'][data-shown='false']`).first().click();
                await rerendered(t.i, back).catch(() => {});
            } else { row.hide = 'no toggle'; row.yamlHidden = '-'; }

            // collapse the first note
            {
                const before = await svgHtml(t.i);
                await hover(t.i, 0);
                await page.waitForFunction(s => Array.from(document.querySelectorAll(s + " [data-note-btn='minus']")).some(b => b.style.opacity === '1'),
                    sel(t.i), { polling: 200, timeout: 5000 });
                await page.evaluate(s => {
                    const b = Array.from(document.querySelectorAll(s + " [data-note-btn='minus']")).find(b => b.style.opacity === '1');
                    b.querySelector('rect').dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
                }, sel(t.i));
                await rerendered(t.i, before);
                row.collapse = (await headerKeys(t.i)).join('|') || 'none';
                const tip = await page.evaluate(s => Array.from(document.querySelectorAll(s + ' svg title')).map(x => x.textContent).join('\n'), sel(t.i));
                row.tip = tip ? tagIn(tip) : 'no title';
            }
        } catch (e) {
            row.error = String(e.message || e).split('\n')[0].slice(0, 120);
        }
        rows.push(row);
    }
    await browser.close();

    const cols = ['page', 'name', 'form', 'ink', 'keys', 'yaml', 'copy', 'payload', 'yamlOn', 'hide', 'yamlHidden', 'collapse', 'tip', 'error'];
    for (const r of rows) console.log(cols.filter(c => r[c] !== undefined).map(c => `${c}=${r[c]}`).join('  '));

    // Twins: the old: and new: scenario of one shape must agree on every behaviour column.
    const behaviour = ['yaml', 'copy', 'payload', 'yamlOn', 'hide', 'yamlHidden', 'collapse', 'tip', 'error'];
    const twins = {};
    // The report capitalises scenario titles ("Old: …"), so the match ignores case.
    const SHAPES = /(old|new)(-plain|-noflow)?: (POST with headers and JSON bodies|GET whose request note is headers only|GET with a full-path block)/i;
    for (const r of rows) {
        const m = SHAPES.exec(r.full);
        if (!m) continue;
        const key = m[3] + (m[2] ? ' [' + m[2].slice(1) + ']' : '');
        (twins[key] = twins[key] || {})[m[1].toLowerCase()] = r;
    }
    for (const [shape, pair] of Object.entries(twins)) {
        if (!pair.old || !pair.new) { console.log(`TWIN ${shape}: missing ${pair.old ? 'new' : 'old'}`); continue; }
        const diff = behaviour.filter(c => String(pair.old[c]) !== String(pair.new[c]));
        console.log(`TWIN ${label} | ${shape}: ${diff.length ? 'DIFFER on ' + diff.map(c => `${c} (old ${pair.old[c]}, new ${pair.new[c]})`).join('; ') : 'agree'}`);
    }
    console.log(`page errors: ${errors.length}${errors.length ? ' - ' + errors.slice(0, 3).join(' | ') : ''}`);
})().catch(e => { console.error(e); process.exit(1); });
