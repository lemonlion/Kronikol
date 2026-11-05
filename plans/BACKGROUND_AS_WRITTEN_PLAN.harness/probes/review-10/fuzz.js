// Random reader sequences against what the intent says, on this cut, with or without the suggested fixes. What it flags is a sequence to look at by hand.
// usage: node fuzz.js <shape> <seeds> [fix] [startSeed]
const { open, chromium, launchInstalled } = require('<review-10>/lib.js');
const fs = require('fs');
const src = fs.readFileSync('<review-10>/s5.js', 'utf8');
const FIX = eval(src.slice(src.indexOf('const FIX = ') + 'const FIX = '.length, src.indexOf('async function fix(t)')).trim().replace(/;$/, ''));

const SHAPES = {
    nested: { rows: 'abcde', words: { zebra: 'b', stripe: 'd', customer: 'a' }, hold: {} },
    held: { rows: 'abcde', words: { zebra: 'b', stripe: 'd', customer: 'a' }, hold: {} },
    fail: { rows: 'abcdefgh', words: { lynx: 'g', okapi: 'h', customer: 'a' }, hold: { b: ['bg'], f: ['c0', 'c00'] } },
};
const DEFAULT = { bg: '0', st: '1', fr: '1', c0: '0', c00: '0' };

function rng(seed) { let s = seed * 2654435761 >>> 0 || 1; return () => { s ^= s << 13; s >>>= 0; s ^= s >>> 17; s ^= s << 5; s >>>= 0; return s / 4294967296; }; }
const parse = (st) => Object.fromEntries(st.split(' ').map((kv) => kv.split('=')));

async function act(page, code, arg) { return page.evaluate(code, arg); }
const TOGGLE = ([kind]) => {
    const g = document.querySelector('details.scenario-parameterized');
    const p = [...g.querySelectorAll('.param-detail-panel')].find((x) => x.style.display !== 'none');
    const find = { bg: () => p.querySelector('details.scenario-background'), st: () => p.querySelector('details.scenario-steps'), fr: () => p.querySelector('details.failure-result'),
        c0: () => [...p.querySelectorAll('details.step-collapsible')].find((d) => d.querySelector(':scope > summary').textContent.includes('the customer pays')),
        c00: () => [...p.querySelectorAll('details.step-collapsible')].find((d) => d.querySelector(':scope > summary').textContent.includes('the card is read')) }[kind];
    const d = find();
    if (!d) return false;
    d.open = !d.open;
    return true;
};
const HAND = (letter) => {
    const g = document.querySelector('details.scenario-parameterized');
    [...g.querySelectorAll(`tr[data-row-idx="${letter.charCodeAt(0) - 97}"]`)].find((r) => r.getClientRects().length).click();
};
const SEARCH = (word) => { const bar = document.getElementById('searchbar'); bar.value = word; run_search_scenarios(); };
const STATUS = () => { const name = document.querySelector('details.scenario-parameterized').getAttribute('data-status') || 'Passed'; const b = [...document.querySelectorAll('#filtering-box .status-toggle')].find((x) => x.getAttribute('data-status').toLowerCase() === name.toLowerCase()); toggle_status(b); };

async function sequence(browser, shape, seed, fixed) {
    const cfg = SHAPES[shape];
    const rand = rng(seed + 1);
    const pick = (a) => a[Math.floor(rand() * a.length)];
    const { r, context } = await open(browser, 'new', shape);
    const page = r.page;
    if (fixed) await page.evaluate(FIX);
    const log = [], faults = [];
    const want = {};
    const rows = {};
    const revealed = new Set();
    let toggledHere = new Set();
    const read = async () => {
        await page.waitForTimeout(shape === 'held' ? 70 : 8);
        return parse(await r.state());
    };
    let st = await read();
    rows[st.row] = st;
    const see = (s) => { rows[s.row] = s; };
    const standing = (row) => rows[row] || null;
    const steps = 9 + Math.floor(rand() * 6);
    const fault = (kind, detail) => faults.push({ kind, detail, at: log.length });
    for (let i = 0; i < steps; i++) {
        const roll = rand();
        const q = await page.evaluate(() => document.getElementById('searchbar').value);
        const hash = await page.evaluate(() => location.hash);
        if (roll < 0.35) {
            const others = cfg.rows.split('').filter((x) => x !== st.row);
            const to = pick(others);
            await page.evaluate(HAND, to);
            log.push('hand ' + to);
            const s = await read();
            for (const key of ['bg', 'st', 'fr', 'c0', 'c00']) {
                const v = s[key];
                if (v === '-' || v === 'u') continue;
                if (key === 'c00' && s.c0 === '0') continue;
                const held = (cfg.hold[to] || []).includes(key);
                if (held) {
                    const own = standing(to);
                    if (own && own[key] !== '-' && own[key] !== 'u' && own[key] !== v) fault('held-own', `${to}.${key} was ${own[key]} now ${v}`);
                } else {
                    const expected = key in want ? want[key] : DEFAULT[key];
                    if (v !== expected) fault('hand-carry', `${to}.${key} expected ${expected} (want ${want[key]}) got ${v}`);
                }
            }
            see(s); st = s; toggledHere = new Set();
        } else if (roll < 0.6) {
            const key = pick(['bg', 'st', 'c0', 'c00', 'fr']);
            if (toggledHere.has(key)) continue;
            if (st[key] === '-' || st[key] === 'u') continue;
            if (key === 'c00' && st.c0 === '0') continue;
            if ((cfg.hold[st.row] || []).includes(key)) { /* a held one is the row's own: no want */ }
            await page.evaluate(TOGGLE, [key]);
            log.push(`toggle ${key} on ${st.row}`);
            toggledHere.add(key);
            const s = await read();
            if (!(cfg.hold[st.row] || []).includes(key)) want[key] = s[key];
            see(s); st = s;
        } else if (roll < 0.85) {
            const word = pick([...Object.keys(cfg.words), '']);
            if (word === q) continue;
            const before = st;
            const standsAtTarget = word ? (cfg.words[word] === before.row ? before : standing(cfg.words[word])) : null;
            await page.evaluate(SEARCH, word);
            log.push(word ? `search ${word}` : 'empty');
            const s = await read();
            if (word) {
                const target = cfg.words[word];
                if (s.row !== target) fault('row', `search ${word}: on ${s.row}, not ${target}`);
                for (const key of ['bg', 'st']) {
                    if (s[key] === '-') continue;
                    const id = target + ':' + key;
                    if (!revealed.has(id)) {
                        if (s[key] !== '1') fault('reveal', `${id} not opened`);
                        revealed.add(id);
                    } else if (standsAtTarget && standsAtTarget[key] !== s[key]) fault('reveal-once', `${id} was ${standsAtTarget[key]} now ${s[key]}`);
                }
                for (const key of ['c0', 'c00', 'fr']) {
                    if (s[key] === '-' || s[key] === 'u' || !standsAtTarget || standsAtTarget[key] === 'u' || standsAtTarget[key] === '-') continue;
                    if (standsAtTarget[key] !== s[key]) fault('stands', `${target}.${key} stood ${standsAtTarget[key]} now ${s[key]}`);
                }
            } else {
                revealed.clear();
                if (s.row !== before.row) {
                    const stands = standing(s.row);
                    if (stands) for (const key of ['bg', 'st', 'c0', 'c00', 'fr']) {
                        if (s[key] === '-' || s[key] === 'u' || stands[key] === 'u' || stands[key] === '-') continue;
                        if (stands[key] !== s[key]) fault('put-back', `${s.row}.${key} stood ${stands[key]} now ${s[key]}`);
                    }
                }
            }
            if (s.row !== before.row) toggledHere = new Set();
            see(s); st = s;
        } else if (roll < 0.9) {
            await page.evaluate(() => clear_all_filters());
            log.push('Clear All');
            revealed.clear();
            const s = await read();
            if (s.row !== st.row) fault('clear-all', `moved ${st.row} to ${s.row}`);
            see(s); st = s;
        } else if (roll < 0.95) {
            await page.evaluate(STATUS);
            log.push('status Passed');
            const s = await read();
            see(s); st = s;
        } else {
            if (!hash) continue;
            await page.goBack();
            await page.waitForTimeout(120);
            log.push('Back');
            const q2 = await page.evaluate(() => document.getElementById('searchbar').value);
            const s = await read();
            const moved = s.row !== st.row;
            const before = st;
            if (q2 !== q) {
                revealed.clear();
                if (q2) for (const key of ['bg', 'st']) revealed.add(s.row + ':' + key);
            } else if (q2) {
                // the same search still on: nothing it already opened opens again
                const stands = moved ? standing(s.row) : before;
                for (const key of ['bg', 'st']) {
                    if (s[key] === '-' || !stands) continue;
                    const id = s.row + ':' + key;
                    if (revealed.has(id) && stands[key] !== s[key]) fault('back-same-search', `${id} was ${stands[key]} now ${s[key]}`);
                }
            }
            if (moved) toggledHere = new Set();
            see(s); st = s;
        }
        if (faults.length) break;
    }
    if (r.errors.length) faults.push({ kind: 'page-error', detail: r.errors.join(' | '), at: log.length });
    await context.close();
    return { seed, log, faults };
}

(async () => {
    const [shape, count, mode, start] = [process.argv[2], Number(process.argv[3]), process.argv[4], Number(process.argv[5] || 0)];
    const browser = await launchInstalled(chromium, { headless: true });
    const tally = {};
    const examples = {};
    let runs = 0;
    for (let seed = start; seed < start + count; seed++) {
        let res;
        try { res = await sequence(browser, shape, seed, mode === 'fix'); } catch (e) { res = { seed, log: [], faults: [{ kind: 'threw', detail: String(e.message).split('\n')[0] }] }; }
        runs++;
        for (const f of res.faults) {
            tally[f.kind] = (tally[f.kind] || 0) + 1;
            const list = examples[f.kind] || (examples[f.kind] = []);
            if (list.length < 6 || res.log.length < list[list.length - 1].log.length) {
                list.push({ seed, log: res.log, detail: f.detail });
                list.sort((a, b) => a.log.length - b.log.length);
                list.length = Math.min(list.length, 6);
            }
        }
    }
    await browser.close();
    console.log(`${shape} ${mode || 'nofix'}: ${runs} sequences, faults ${JSON.stringify(tally)}`);
    for (const kind of Object.keys(examples)) {
        console.log('--- ' + kind);
        for (const e of examples[kind]) console.log(`  [${e.seed}] ${e.log.join(', ')}  => ${e.detail}`);
    }
})().catch((e) => { console.error(e); process.exit(1); });
