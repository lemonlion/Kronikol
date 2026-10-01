'use strict'
// Jest setupFilesAfterEnv: every expect becomes an assertion note; when the test file ends, its spans become one
// OTLP/JSON line of spans-<worker>.jsonl, and the process-wide subscriptions are undone so the next file in this
// worker starts clean.
const fs = require('node:fs')
const path = require('node:path')
const { JsonTraceSerializer } = require('@opentelemetry/otlp-transformer')
const { exporter, fastifyOtel } = require('./tracing')
const { stopCapture } = require('./capture')

const dir = process.env.KRONIKOL_DIR ?? 'kronikol-capture'

afterAll(() => {
  const spans = exporter.getFinishedSpans()
  if (spans.length > 0) {
    fs.mkdirSync(dir, { recursive: true })
    const line = Buffer.from(JsonTraceSerializer.serializeRequest(spans)).toString('utf8')
    fs.appendFileSync(path.join(dir, `spans-${process.env.JEST_WORKER_ID ?? '1'}.jsonl`), line + '\n')
  }
  exporter.reset()
  fastifyOtel.disable()
  stopCapture()
})

// Each matcher call an assertion: its text the test's own line (expect(…).toBe(…)), its place that line's.
const plain = text => String(text).replace(/\u001b\[[0-9;]*m/g, '')

function callSite () {
  for (const frame of new Error().stack.split('\n').slice(2)) {
    const at = /\(?([^()\s]+):(\d+):\d+\)?$/.exec(frame)
    if (!at || at[1].includes('node_modules') || at[1] === __filename || at[1].startsWith('node:')) continue
    const line = fs.readFileSync(at[1], 'utf8').split('\n')[Number(at[2]) - 1]?.trim()
    return { text: line, sourceFile: path.relative(process.cwd(), at[1]).split(path.sep).join('/'), sourceLine: Number(at[2]) }
  }
  return { text: 'assertion' }
}

function note (site, error) {
  if (!globalThis.__kronikolTestId) return
  fs.appendFileSync(path.join(dir, 'tests.ndjson'), JSON.stringify({
    event: 'assertion', testId: globalThis.__kronikolTestId, attempt: globalThis.__kronikolAttempt, ...site,
    status: error ? 'failed' : 'passed', error: error ? plain(error.message) : undefined, timestamp: new Date().toISOString()
  }) + '\n')
}

function noting (matchers, site) {
  return new Proxy(matchers, {
    get (target, property, receiver) {
      const value = Reflect.get(target, property, receiver)
      if (property === 'not' || property === 'resolves' || property === 'rejects') return noting(value, site)
      if (typeof value !== 'function') return value
      return (...args) => {
        let result
        try {
          result = value.apply(target, args)
        } catch (error) {
          note(site, error)
          throw error
        }
        if (typeof result?.then !== 'function') {
          note(site)
          return result
        }
        return result.then(r => { note(site); return r }, error => { note(site, error); throw error })
      }
    }
  })
}

globalThis.expect = new Proxy(globalThis.expect, {
  apply: (target, thisArg, args) => noting(Reflect.apply(target, thisArg, args), callSite())
})
