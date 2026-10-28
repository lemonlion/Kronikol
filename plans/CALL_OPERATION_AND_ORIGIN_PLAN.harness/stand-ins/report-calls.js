'use strict'
// One line per report data file (TestRunReport.json in its plain JSON form): version, scenarios, and the
// request records under features[].scenarios[].httpInteractions[], with callers, services and schemes. Counts only.
// usage: node report-calls.js <file> [<file> ...]
const fs = require('node:fs')
for (const file of process.argv.slice(2)) {
  let root
  try {
    root = JSON.parse(fs.readFileSync(file, 'utf8').replace(/^﻿/, ''))
  } catch (e) {
    console.log(`${file}: not plain JSON (${e.message.slice(0, 60)})`)
    continue
  }
  let scenarios = 0, withKey = 0, req = 0, resp = 0
  const callers = {}, schemes = {}, attr = {}
  for (const f of root.features ?? []) {
    for (const s of f.scenarios ?? []) {
      scenarios++
      if (!Array.isArray(s.httpInteractions)) continue
      withKey++
      for (const i of s.httpInteractions) {
        if (i.type === 'Response') { resp++; continue }
        if (i.type !== 'Request') continue
        req++
        const k = `${i.callerName}>${i.serviceName}`
        callers[k] = (callers[k] || 0) + 1
        const scheme = String(i.uri ?? '').split(':')[0]
        schemes[scheme] = (schemes[scheme] || 0) + 1
        const a = i.attributionSource === undefined ? '(absent)' : String(i.attributionSource)
        attr[a] = (attr[a] || 0) + 1
      }
    }
  }
  const top = Object.entries(callers).sort((a, b) => b[1] - a[1]).slice(0, 8).map(([k, n]) => `${k} x${n}`).join('; ')
  console.log(`${file}: v=${root.kronikolVersion} fmt=${root.formatVersion ?? '-'} features=${(root.features ?? []).length} scenarios=${scenarios} withHttpInteractions=${withKey} requests=${req} responses=${resp} schemes=${JSON.stringify(schemes)} attributionSource=${JSON.stringify(attr)} callers: ${top}`)
}
