// Third P3 audit: does the browser's fragment splitter read captured note text as diagram syntax?
// Loads the shipped render script into a page and calls window._splitDiagramSource on sources in Kronikol's form.
const path = require('path'), url = require('url');
const pw = require(path.join(path.resolve(__dirname, '..', '..'), 'tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));

const PREFIX = ['@startuml', '!pragma teoz true', 'skinparam wrapWidth 800', 'autonumber 1', '',
    'participant "Caller" as Caller', 'participant "Orders API" as OrdersAPI'];

// n request/response pairs; the request note carries `noteLines` (captured text, escaped as Kronikol writes it).
function sequence(n, noteLines, extra) {
    const body = [];
    for (let i = 0; i < n; i++) {
        body.push(`Caller -[#438DD5]> OrdersAPI: GET: /orders/${i}`);
        body.push('note left');
        body.push(...noteLines);
        body.push('end note');
        body.push('OrdersAPI -[#438DD5]-> Caller: 200');
        if (extra) body.push(...extra(i));
    }
    return [...PREFIX, ...body, '@enduml'].join('\n');
}

(async () => {
    const browser = await pw.chromium.launch({ headless: true });
    const page = await (await browser.newContext()).newPage();
    await page.goto(url.pathToFileURL(path.resolve(__dirname, 'split-page.html')).href);
    await page.waitForFunction(() => typeof window._splitDiagramSource === 'function');
    const run = (src, h) => page.evaluate(([s, h]) => window._splitDiagramSource(s, h), [src, h]);

    // 1. The autonumber each fragment starts at: plain notes, then notes holding captured arrows.
    for (const [label, lines] of [
        ['plain note', ['{', '  "id": 1', '}']],
        ['note with -> and -->', ['<!-- a comment -->', 'a -> b', 'x --> y']],
    ]) {
        const frags = await run(sequence(12, lines), 400);
        const starts = frags.map(f => (f.match(/autonumber (\d+)/) || [])[1]);
        const arrows = frags.map(f => f.split('\n').filter(l => /^\S+ -\[#[0-9A-F]+\]-?> /.test(l)).length);
        console.log(`${label}: ${frags.length} fragments, autonumber starts ${starts.join(',')}, real arrows per fragment ${arrows.join(',')}`);
    }

    // 2. An assertion note (hnote across … end note) whose captured message holds an arrow: where do fragments cut?
    const assertion = i => ['hnote across <<assertionNote>> #F8D7DA', `✗ Expected order ${i} -> shipped`, 'but it went a --> b', 'end note'];
    const frags = await run(sequence(12, ['{', '  "id": 1', '}'], assertion), 400);
    const broken = frags.map((f, k) => {
        let open = 0;
        for (const l of f.split('\n')) {
            const t = l.trim();
            if (/^(h|r)?note\b/.test(t) && !/:/.test(t.split(/\s+/).slice(0, 3).join(' '))) open++;
            if (/^end ?(h|r)?note$/i.test(t)) open--;
        }
        return open !== 0 ? `fragment ${k + 1} unbalanced notes (${open})` : null;
    }).filter(Boolean);
    console.log(`assertion notes holding arrows: ${frags.length} fragments; ${broken.length ? broken.join('; ') : 'every fragment balanced'}`);
    if (broken.length) {
        const f = frags.find((f, k) => broken[0].startsWith(`fragment ${k + 1}`));
        console.log('  tail of the first unbalanced fragment: ' + JSON.stringify(f.split('\n').slice(-4)));
    }
    await browser.close();
})().catch(e => { console.log('PROBE FAILED ' + e.message); process.exit(1); });
