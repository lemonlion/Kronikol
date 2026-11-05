// Writes the input of IngestCommandTests.A_file_attached_to_a_step_two_tests_read_alike_on_stops_no_report
const fs = require('fs'), path = require('path');
const dir = process.argv[2];
const captures = path.join(dir, 'captures-attached');
fs.mkdirSync(captures, { recursive: true });
fs.writeFileSync(path.join(captures, 'captures.ndjson'), '');
const files = path.join(dir, 'attached'); fs.mkdirSync(files, { recursive: true });
const first = path.join(files, 'a.txt').replace(/\\/g, '/'), second = path.join(files, 'b.txt').replace(/\\/g, '/');
fs.writeFileSync(first, 'first'); fs.writeFileSync(second, 'second');
const lines = [
  { event: 'start', testId: 'a', testName: 'The order goes through', feature: 'Orders', timestamp: '2026-08-21T10:00:00Z' },
  { event: 'step', testId: 'a', keyword: 'Given', text: 'the basket holds apple', status: 'passed', timestamp: '2026-08-21T10:00:01Z' },
  { event: 'step', testId: 'a', keyword: 'When', text: 'the customer places the order', status: 'passed', timestamp: '2026-08-21T10:00:02Z' },
  { event: 'step', testId: 'a', keyword: 'Then', text: 'the order is confirmed', status: 'passed', timestamp: '2026-08-21T10:00:03Z' },
  { event: 'attachment', testId: 'a', name: 'a.txt', path: first, step: 0, timestamp: '2026-08-21T10:00:03Z' },
  { event: 'end', testId: 'a', status: 'passed', timestamp: '2026-08-21T10:00:04Z' },
  { event: 'start', testId: 'b', testName: 'The payment is declined', feature: 'Orders', timestamp: '2026-08-21T10:00:05Z' },
  { event: 'step', testId: 'b', keyword: 'Given', text: 'the basket holds apple', status: 'passed', timestamp: '2026-08-21T10:00:06Z' },
  { event: 'step', testId: 'b', keyword: 'When', text: 'the customer places the order', status: 'failed', error: '402 card declined', timestamp: '2026-08-21T10:00:07Z' },
  { event: 'attachment', testId: 'b', name: 'b.txt', path: second, step: 0, timestamp: '2026-08-21T10:00:07Z' },
  { event: 'end', testId: 'b', status: 'failed', error: '402 card declined', timestamp: '2026-08-21T10:00:08Z' },
];
fs.writeFileSync(path.join(captures, 'tests.ndjson'), lines.map(l => JSON.stringify(l)).join('\n') + '\n');
console.log(captures);
