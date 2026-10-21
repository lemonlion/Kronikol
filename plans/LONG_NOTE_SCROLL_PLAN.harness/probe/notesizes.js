// Aggregates of a real report's payload notes, and an ESTIMATE of how its diagrams split with every note expanded,
// today and under the plan's cap. Prints numbers only, never content.
// Usage: node notesizes.js <TestRunReport.html> [cap=40]
//
// The estimate follows the page's splitter on the stored source: units are a call (a message that is not a return)
// with the returns and notes after it; a part is cut when the next unit would take it past 12,000 px (45 px per arrow
// and per note, 18 px per note line); a note over 15,000 characters adds one forced cut per extra 15,000. It reads the
// JSON view the source holds (the page shows YAML by default, whose line count differs) and does not model blocks.
const fs = require('fs');
const zlib = require('zlib');

const html = fs.readFileSync(process.argv[2], 'utf8');
const cap = parseInt(process.argv[3] || '40', 10);
const m = html.match(/<script id="puml-data" type="application\/json">([\s\S]*?)<\/script>/);
if (!m) { console.log('no puml-data'); process.exit(1); }
const data = JSON.parse(m[1]);
const sources = Object.values(data).map(z => zlib.gunzipSync(Buffer.from(z, 'base64')).toString('utf8'));
const META = /^(<color:(?:gray|#[0-9A-Fa-f]{6})>|<b>Key<\/b> )/;
const ARROW = /<{1,2}[-=.]{1,2}(?:\[[^\]]*\])?[-=.]{0,2}|[-=.]{1,2}(?:\[[^\]]*\])?[-=.]{0,2}>{1,2}/;
const RETURN = /-(?:\[[^\]]*\])?->/;

const notes = [];
const tally = { diagrams: 0, today: { split: 0, parts: 0 }, capped: { split: 0, parts: 0 } };
for (const src of sources) {
    if (!/^@startuml/m.test(src) || !/->/.test(src)) continue;
    tally.diagrams++;
    const lines = src.replace(/\r\n/g, '\n').split('\n');
    const units = [];
    let unit = null;
    for (let i = 0; i < lines.length; i++) {
        const t = lines[i].trim();
        if (/^note(?:<<[^>]*>>)*\s+(left|right)/.test(t)) {
            let j = i + 1;
            const body = [];
            while (j < lines.length && lines[j].trim() !== 'end note') body.push(lines[j++]);
            let meta = 0;
            while (meta < body.length && META.test(body[meta].trim())) meta++;
            if (meta > 0 && meta < body.length && body[meta].trim() === '') meta++;
            const bodyLines = body.length - meta;
            const chars = body.join('\n').length;
            notes.push({ bodyLines, chars, side: /right/.test(t) ? 'response' : 'request' });
            const drawnCapped = bodyLines > cap ? meta + (meta ? 0 : 1) + cap + 2 : body.length;
            if (!unit) units.push(unit = { today: 0, capped: 0, forced: 0 });
            unit.today += 45 + 18 * body.length;
            unit.capped += 45 + 18 * drawnCapped;
            unit.forced += Math.max(0, Math.ceil(chars / 15000) - 1);
            i = j;
        } else if (ARROW.test(t) && !/^[hr]?note\b|^'/.test(t)) {
            if (!RETURN.test(t) || !unit) units.push(unit = { today: 0, capped: 0, forced: 0 });
            unit.today += 45; unit.capped += 45;
        }
    }
    for (const k of ['today', 'capped']) {
        let parts = 1, height = 0;
        for (const u of units) {
            if (height > 0 && height + u[k] > 12000) { parts++; height = 0; }
            height += u[k];
            if (k === 'today' && u.forced) { parts += u.forced; height = 0; }
        }
        tally[k].parts += parts;
        if (parts > 1) tally[k].split++;
    }
}
const L = notes.map(n => n.bodyLines).sort((a, b) => a - b), C = notes.map(n => n.chars).sort((a, b) => a - b);
const pct = (arr, p) => arr[Math.min(arr.length - 1, Math.floor(arr.length * p))];
const over = n => notes.filter(x => x.bodyLines > n).length;
console.log(JSON.stringify({
    diagrams: tally.diagrams, notes: notes.length,
    bodyLines: { p50: pct(L, 0.5), p90: pct(L, 0.9), p99: pct(L, 0.99), max: L[L.length - 1] },
    chars: { p50: pct(C, 0.5), p90: pct(C, 0.9), p99: pct(C, 0.99), max: C[C.length - 1] },
    notesOverBodyLines: { 10: over(10), 20: over(20), 40: over(40), 60: over(60), 100: over(100), 300: over(300) },
    responsesOver40: notes.filter(x => x.bodyLines > 40 && x.side === 'response').length,
    notesOver15000Chars: notes.filter(x => x.chars > 15000).length,
    estimateEveryNoteExpanded: { today: tally.today, cappedAt: cap, capped: tally.capped },
}, null, 1));
