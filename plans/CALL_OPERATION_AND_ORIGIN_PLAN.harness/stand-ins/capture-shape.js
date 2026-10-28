'use strict'
// Aggregates of a Kronikol capture folder (interactions-*.ndjson, tests.ndjson, spans-*.jsonl): counts and
// short keys only, never a whole body.
const fs = require('node:fs')
const path = require('node:path')
const dir = process.argv[2]
const lines = f => fs.readFileSync(f, 'utf8').split('\n').filter(l => l.trim().length > 0).map(l => JSON.parse(l))
for (const f of fs.readdirSync(dir).sort()) {
  const full = path.join(dir, f)
  if (f.startsWith('interactions')) {
    const rs = lines(full)
    const groups = {}
    for (const r of rs) {
      const k = `${r.type} ${r.callerName}>${r.serviceName} ${r.method} ${r.uri.replace(/:\d+\//, ':<port>/')} status=${r.statusCode ?? '-'} body=${r.content ? r.content.length : 0} trace=${r.activityTraceId ? 'y' : 'n'}`
      groups[k] = (groups[k] || 0) + 1
    }
    console.log(`${f}: ${rs.length} lines; keys ${[...new Set(rs.flatMap(r => Object.keys(r)))].join(',')}`)
    for (const [k, n] of Object.entries(groups)) console.log(`  ${k}  x${n}`)
    console.log(`  test ids ${new Set(rs.map(r => r.testId)).size}; request bodies: ${[...new Set(rs.filter(r => r.type === 'Request' && r.content).map(r => r.content.slice(0, 90)))].join(' | ')}`)
  } else if (f.startsWith('tests')) {
    const rs = lines(full)
    const byEvent = {}
    for (const r of rs) byEvent[`${r.event} attempt=${r.attempt} ${r.status ?? ''}`.trim()] = (byEvent[`${r.event} attempt=${r.attempt} ${r.status ?? ''}`.trim()] || 0) + 1
    console.log(`${f}: ${rs.length} lines; ${JSON.stringify(byEvent)}; tests ${[...new Set(rs.filter(r => r.event === 'start').map(r => r.testName))].join(' | ')}`)
  } else if (f.startsWith('spans')) {
    const rs = lines(full)
    let n = 0
    const scopes = {}
    for (const r of rs) for (const rsp of r.resourceSpans) for (const ss of rsp.scopeSpans) { scopes[ss.scope.name] = (scopes[ss.scope.name] || 0) + ss.spans.length; n += ss.spans.length }
    console.log(`${f}: ${rs.length} lines, ${n} spans ${JSON.stringify(scopes)}`)
  }
}
