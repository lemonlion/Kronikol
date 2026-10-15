// The wiki's reporter recipe (Integration-Playwright.md, "Screenshots and artefacts"), reduced to start/attachment/end.
const crypto = require('crypto'), fs = require('fs'), path = require('path');
const out = path.resolve(process.env.PROBE_OUT || 'out'); fs.mkdirSync(path.join(out, 'inline'), { recursive: true });
const file = path.join(out, 'tests.jsonl'); fs.writeFileSync(file, '');
let n = 0;
const append = r => fs.appendFileSync(file, JSON.stringify(r) + '\n');
const idOf = t => crypto.createHash('md5').update(t.id).digest('hex');
const writeInlineBody = a => { const p = path.join(out, 'inline', `${n++}-${a.name}`); fs.writeFileSync(p, a.body); return p; };
class KronikolReporter {
  onTestBegin(test, result) {
    append({ event: 'start', testId: idOf(test), testName: test.title, feature: test.parent.title,
             attempt: result.retry + 1, timestamp: new Date().toISOString() });
  }
  onTestEnd(test, result) {
    const testId = idOf(test);
    for (const a of result.attachments) {
      if (a.name === 'kronikol-test-id') continue;
      const p = a.path ?? writeInlineBody(a);
      append({ event: 'attachment', testId, name: a.name, path: p, mediaType: a.contentType,
               attempt: result.retry + 1, timestamp: new Date().toISOString() });
    }
    append({ event: 'end', testId, status: result.status, durationMs: result.duration,
             error: result.error?.message, attempt: result.retry + 1, timestamp: new Date().toISOString() });
  }
}
module.exports = KronikolReporter;
