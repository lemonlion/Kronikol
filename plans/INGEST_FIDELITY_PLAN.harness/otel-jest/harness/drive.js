'use strict'
// One GraphQL call through fastify.inject, the way a component test drives the service in-process,
// with the harness's own capture hook (the picture's "Fastify hooks") recording what it can see.
const { trace } = require('@opentelemetry/api')

function addCaptureHook (app, testIds, seen) {
  app.addHook('onRequest', async (request) => {
    const span = trace.getActiveSpan()
    seen.push({
      at: 'fastify onRequest (harness hook)',
      url: request.url,
      activeTraceId: span?.spanContext().traceId ?? null,
      activeSpanId: span?.spanContext().spanId ?? null,
      activeSpanName: span?.name ?? null,
      testId: testIds.getStore()?.testId ?? null,
      // The wall clock a harness stamps on its records, against the span's own start (OpenTelemetry JS
      // takes performance.timeOrigin + performance.now()): internal flow matches spans to a call's window.
      wallClockMs: Date.now(),
      spanStartMs: span?.startTime ? span.startTime[0] * 1e3 + span.startTime[1] / 1e6 : null
    })
  })
}

async function chargeQuery (app, id) {
  const res = await app.inject({
    method: 'POST',
    url: '/graphql',
    payload: { query: `query Charge { charge(id: "${id}") { id amount fee status receipt { number } } }` }
  })
  return JSON.parse(res.body)
}

module.exports = { addCaptureHook, chargeQuery }
