'use strict'
// Key paths of a JSON file with how often each occurs (array indices collapsed to []), and for scalar leaves the
// value type. Prints no values except for the keys named in SHOW (short scalars: versions, names of formats).
// usage: node json-shape.js <file> [maxDepth]
const fs = require('node:fs')
const file = process.argv[2]
const maxDepth = Number(process.argv[3] ?? 6)
const SHOW = new Set(['kronikolVersion', 'formatVersion', 'mergeableFormatVersion', 'version', 'generator', 'producer', 'type', 'attributionSource', 'phase', 'dependencyCategory', 'metaType', 'capturedBy', 'callerName', 'serviceName', 'method'])
const root = JSON.parse(fs.readFileSync(file, 'utf8').replace(/^﻿/, ''))
const counts = new Map()
const values = new Map()
function walk (v, p, depth) {
  const key = p || '$'
  const kind = Array.isArray(v) ? 'array' : v === null ? 'null' : typeof v
  const k = `${key} : ${kind}`
  counts.set(k, (counts.get(k) || 0) + 1)
  if (kind === 'array') {
    if (depth < maxDepth) for (const item of v) walk(item, key + '[]', depth + 1)
  } else if (kind === 'object') {
    if (depth < maxDepth) for (const [name, child] of Object.entries(v)) walk(child, key + '.' + name, depth + 1)
  } else {
    const last = key.split('.').pop()
    if (SHOW.has(last) && String(v).length <= 80) {
      if (!values.has(key)) values.set(key, new Map())
      const m = values.get(key)
      m.set(String(v), (m.get(String(v)) || 0) + 1)
    }
  }
}
walk(root, '', 0)
for (const [k, n] of counts) console.log(`${String(n).padStart(6)}  ${k}`)
for (const [k, m] of values) console.log(`values ${k}: ${[...m].slice(0, 12).map(([v, n]) => `${v} x${n}`).join(', ')}`)
