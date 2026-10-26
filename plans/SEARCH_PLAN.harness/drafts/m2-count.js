// M2 (SEARCH_PLAN 4.4, S3): how often tag:, status: and feature: followed by a letter or digit stand in the text the
// instant search reads (data-search and data-row-search) of real reports, before those keys become forms of the query.
// 5.0.0 held dep: and duration: to the same rule (D16: "must not clash with real searches", measured at 0).
// Prints numbers and the shape of each clash only (which key, where it stands, what kind of value), never the text.
// A compressed page (no data-search in its file) is opened in the bundled Chromium, never an installed browser
// (CLAUDE.md), and read once it has decoded itself.
//   node m2-count.js            every source below, as JSON on stdout
const fs = require('fs'), path = require('path'), crypto = require('crypto');

const ROOT = 'C:/Code';
function glob(dir, re) {
    let names;
    try { names = fs.readdirSync(dir); } catch (e) { return []; }
    return names.filter(n => re.test(n)).map(n => path.join(dir, n).replace(/\\/g, '/'));
}
function worktrees() {
    return fs.readdirSync(ROOT).filter(n => /^Kronikol(-|$)/.test(n) && !/\.wiki/.test(n)).map(n => ROOT + '/' + n);
}

const BP_NET = ROOT + '/BreakfastProvider-cmock/tests/BreakfastProvider.Tests.Component.ReqNRoll/bin/Debug/net10.0/Reports/';
const EXAMPLES = ROOT + '/Kronikol/examples/Example.Api/tests/';
const SOURCES = {
    'BreakfastProvider (.NET, ReqNRoll, 4.26.4)': [BP_NET + 'TestRunReport.html', BP_NET + 'Specifications.html'],
    'BreakfastProvider (Java, Kronikol4J 0.1.24)': ['component-cucumber', 'component-junit5', 'component-spock', 'component-testng']
        .map(t => ROOT + '/BreakfastProvider/java/tests/' + t + '/target/Reports/TestRunReport.html'),
    'Example.Api reports (5.8.0, compressed)': ['Example.Api.Tests.Component.LightBDD.xUnit3', 'Example.Api.Tests.Component.ReqNRoll.xUnit3',
        'Example.Api.Tests.Component.xUnit3']
        .flatMap(t => ['TestRunReport.html', 'Specifications.html'].map(f => EXAMPLES + t + '/bin/Debug/net10.0/Reports/' + f)),
    "Kronikol's own test-run reports": [ROOT + '/Kronikol/tests/Kronikol.Tests/bin/Debug/net10.0/Reports/TestRunReport.html',
        ROOT + '/Kronikol/plans/TOOLBAR_AT_EVERY_WIDTH_PLAN.harness/out-audit/TestRunReport.html'],
    'E2E fixtures (PlaywrightOutput of every worktree)': worktrees()
        .flatMap(w => glob(w + '/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/PlaywrightOutput', /\.html$/i)),
};

const KEYS = ['tag', 'status', 'feature', 'dep', 'dependency', 'duration', 'is'];
const RESULTS = new Set(['passed', 'failed', 'skipped', 'bypassed', 'skippedafterfailure']);
const VERDICTS = new Set(['stable', 'new', 'broke', 'failing', 'always-failing', 'fixed', 'flaky', 'slower',
    'behaviour-changed', 'reordered', 'alternating', 'unstable-shape', 'quarantined', 'unknown']);

function decode(s) {
    return s.replace(/&(#x[0-9a-f]+|#\d+|quot|amp|lt|gt|apos);/gi, (m, e) => {
        e = e.toLowerCase();
        if (e === 'quot') return '"';
        if (e === 'amp') return '&';
        if (e === 'lt') return '<';
        if (e === 'gt') return '>';
        if (e === 'apos') return "'";
        if (e[1] === 'x') return String.fromCodePoint(parseInt(e.slice(2), 16));
        return String.fromCodePoint(parseInt(e.slice(1), 10));
    });
}
function texts(html) {
    const out = [], re = /\sdata-(?:row-)?search="([^"]*)"/g;
    let m;
    while ((m = re.exec(html))) out.push(decode(m[1]));
    return out;
}

// the shape of a value: what a reader would have typed after the colon to find this text
function valueShape(key, v) {
    if (key === 'status' && RESULTS.has(v)) return 'an execution result';
    if (key === 'status' && VERDICTS.has(v)) return 'a history verdict name';
    if (/^\d+$/.test(v)) return 'digits';
    if (/^\p{L}+$/u.test(v)) return 'letters';
    return 'letters, digits and punctuation';
}
function keyTally(tally, key) {
    return tally.keys[key] || (tally.keys[key] = { total: 0, atWordStart: 0, minus: 0, insideAWord: 0, shapes: {} });
}
function count(list, tally) {
    for (const t of list) {
        tally.texts++;
        for (const key of KEYS) {
            // standing on its own: at the start, or after a space, a bracket, a quote or punctuation
            const own = new RegExp('(^|[^\\p{L}\\p{N}_-])(-?)' + key + ':([\\p{L}\\p{N}][^\\s()"]*)', 'giu');
            let m;
            while ((m = own.exec(t))) {
                const k = keyTally(tally, key);
                k.total++;
                const wordStart = m[1] === '' || /[\s()"]/.test(m[1]);
                if (wordStart) k.atWordStart++;
                if (m[2] === '-') k.minus++;
                const shape = (wordStart ? 'at a word start' : 'after punctuation') + ', ' + valueShape(key, m[3].toLowerCase());
                k.shapes[shape] = (k.shapes[shape] || 0) + 1;
            }
            // inside a word (hashtag:x, httpstatus:200) a key is never read as one: counted apart
            const inside = (t.match(new RegExp('[\\p{L}\\p{N}_]' + key + ':[\\p{L}\\p{N}]', 'giu')) || []).length;
            if (inside) keyTally(tally, key).insideAWord += inside;
        }
    }
}

async function main() {
    const seen = new Set(), report = {}, compressed = [];
    for (const [source, files] of Object.entries(SOURCES)) {
        const tally = report[source] = { files: 0, duplicates: 0, missing: 0, noSearchText: 0, compressed: 0, texts: 0, keys: {} };
        for (const f of files) {
            if (!fs.existsSync(f)) { tally.missing++; continue; }
            const html = fs.readFileSync(f, 'utf8');
            const hash = crypto.createHash('sha1').update(html).digest('hex');
            if (seen.has(hash)) { tally.duplicates++; continue; }
            seen.add(hash);
            tally.files++;
            const list = texts(html);
            if (list.length) count(list, tally);
            else if (html.includes('id="kron-frame-page"')) { tally.compressed++; compressed.push({ f, tally }); }
            else tally.noSearchText++;
        }
    }
    if (compressed.length) {
        const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
        const browser = await pw.chromium.launch();
        const page = await browser.newPage();
        for (const { f, tally } of compressed) {
            await page.goto('file:///' + f, { waitUntil: 'load', timeout: 120000 });
            await page.waitForFunction(() => !!document.querySelector('[data-search]'), null, { timeout: 60000, polling: 200 }).catch(() => {});
            const list = await page.evaluate(() => Array.from(document.querySelectorAll('[data-search],[data-row-search]'))
                .flatMap(e => [e.getAttribute('data-search'), e.getAttribute('data-row-search')].filter(v => v !== null)));
            if (!list.length) tally.compressedUnread = (tally.compressedUnread || 0) + 1;
            count(list, tally);
        }
        await browser.close();
    }
    console.log(JSON.stringify(report, null, 2));
}
main().catch(e => { console.error(e); process.exit(1); });
