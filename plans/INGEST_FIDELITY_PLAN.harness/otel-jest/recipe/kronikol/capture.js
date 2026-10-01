'use strict'
// Capture points: every inbound request Fastify serves and every outbound call MSW sees becomes a
// request line and a response line of interactions-<worker>.ndjson, stamped with the running test's id and
// with the trace and span the call was made under, which joins it to the spans kronikol ingest --spans reads.
const { AsyncLocalStorage } = require('node:async_hooks')
const crypto = require('node:crypto')
const dc = require('node:diagnostics_channel')
const fs = require('node:fs')
const path = require('node:path')
const { trace } = require('@opentelemetry/api')

const dir = process.env.KRONIKOL_DIR ?? 'kronikol-capture'
const file = path.join(dir, `interactions-${process.env.JEST_WORKER_ID ?? '1'}.ndjson`)
const SERVICE = process.env.KRONIKOL_SERVICE ?? 'Service'
const requestScope = new AsyncLocalStorage()

/** The test the request being served arrived under, else the test running now. */
const testId = () => requestScope.getStore()?.testId ?? globalThis.__kronikolTestId ?? 'outside-any-test'

function write (record) {
  fs.mkdirSync(dir, { recursive: true })
  fs.appendFileSync(file, JSON.stringify(record) + '\n')
}

/** The ids of the call: its own, its test's, and the span it is made under. */
function begin () {
  const span = trace.getActiveSpan()?.spanContext()
  return { requestResponseId: crypto.randomUUID(), testId: testId(), activityTraceId: span?.traceId, activitySpanId: span?.spanId }
}

// Inbound: Fastify announces every instance it creates on this channel, so no test registers anything.
function onFastify ({ fastify }) {
  const uri = request => `http://${request.hostname}${request.url}`
  const writeRequest = request => {
    if (request.kronikol.written) return
    request.kronikol.written = true
    const { written, at, ...call } = request.kronikol
    write({ ...call, type: 'Request', method: request.method, uri: uri(request), serviceName: SERVICE, callerName: 'Test',
      content: request.body === undefined ? undefined : JSON.stringify(request.body), timestamp: at })
  }
  fastify.addHook('onRequest', (request, reply, done) => {
    // Everything the service does for this request keeps the test it arrived under, even past the test's end.
    requestScope.run({ testId: testId() }, () => {
      request.kronikol = { ...begin(), at: new Date().toISOString() }
      done()
    })
  })
  // Written once the body is parsed, at the time the request arrived; onResponse writes it for one that never got here.
  fastify.addHook('preHandler', (request, reply, done) => { writeRequest(request); done() })
  fastify.addHook('onSend', (request, reply, payload, done) => {
    request.kronikolBody = typeof payload === 'string' ? payload : undefined
    done()
  })
  fastify.addHook('onResponse', (request, reply, done) => {
    writeRequest(request)
    const { written, at, ...call } = request.kronikol
    // Timed once the service's own onResponse hooks have run, so the spans of its response work are the call's.
    setImmediate(() => write({ ...call, type: 'Response', method: request.method, uri: uri(request), serviceName: SERVICE, callerName: 'Test',
      statusCode: String(reply.statusCode), content: request.kronikolBody, timestamp: new Date().toISOString() }))
    done()
  })
}
dc.subscribe('fastify.initialization', onFastify)

// Outbound: what MSW sees, mocked or passed through. name maps a host to the service it stands for.
function captureMsw (server, name = host => host) {
  const calls = new Map()
  server.events.on('request:start', async ({ request, requestId }) => {
    const at = new Date().toISOString()
    const call = { ...begin(), method: request.method, uri: request.url, serviceName: name(new URL(request.url).host), callerName: SERVICE }
    calls.set(requestId, call)
    const content = await request.clone().text()
    write({ ...call, type: 'Request', content: content || undefined, timestamp: at })
  })
  const respond = async ({ response, requestId }) => {
    const call = calls.get(requestId)
    if (!call) return
    calls.delete(requestId)
    const at = new Date().toISOString()
    const content = await response.clone().text()
    write({ ...call, type: 'Response', statusCode: String(response.status), content: content || undefined, timestamp: at })
  }
  server.events.on('response:mocked', respond)
  server.events.on('response:bypass', respond)
}

function stopCapture () {
  dc.unsubscribe('fastify.initialization', onFastify)
}

module.exports = { captureMsw, stopCapture }
