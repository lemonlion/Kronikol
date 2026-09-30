'use strict'
// V3: two tests' requests in flight at once (test.concurrent, or work outliving a test). Does each
// span carry its own test's id and trace, and do the harness's capture points see the right ones?
const fs = require('node:fs')
const { registerInstrumentations } = require('@opentelemetry/instrumentation')
const { UndiciInstrumentation } = require('@opentelemetry/instrumentation-undici')
const { GraphQLInstrumentation } = require('@opentelemetry/instrumentation-graphql')
const { FastifyOtelInstrumentation } = require('@fastify/otel')
const { testIds, startTracing, patchThroughJest, summarise } = require('../harness/tracing')

const { provider, exporter } = startTracing()
// Fastify publishes its initialization on a process-wide diagnostics channel, so a subscription left by
// one test file registers a second plugin on the next file's instances in the same worker: disable it.
const fastifyOtel = new FastifyOtelInstrumentation({ registerOnInitialization: true })
afterAll(() => fastifyOtel.disable())
registerInstrumentations({
  tracerProvider: provider,
  instrumentations: [new UndiciInstrumentation(), fastifyOtel]
})
patchThroughJest(new GraphQLInstrumentation({ mergeItems: true }), require, provider)

const { startRiskFake, startMsw } = require('../harness/fakes')
const { addCaptureHook, chargeQuery } = require('../harness/drive')
const { buildApp } = require('../src/app')

test('V3: concurrent requests from two tests', async () => {
  const seen = []
  const risk = await startRiskFake()
  const msw = startMsw(testIds, seen)
  const app = buildApp()
  addCaptureHook(app, testIds, seen)
  await app.ready()
  const [a, b] = await Promise.all([
    testIds.run({ testId: 'T-A' }, () => chargeQuery(app, 'a1')),
    testIds.run({ testId: 'T-B' }, () => chargeQuery(app, 'b1'))
  ])
  await app.close(); msw.close(); risk.close()
  const spans = summarise(exporter.getFinishedSpans())
  const byTrace = {}
  for (const s of spans) (byTrace[s.traceId] ??= new Set()).add(s.testId)
  const mixedTraces = Object.values(byTrace).filter(ids => ids.size > 1).length
  fs.writeFileSync(`${__dirname}/../results/v3-concurrency.json`, JSON.stringify({ a, b, seen, mixedTraces, traces: Object.keys(byTrace).length, spans }, null, 2))
  expect(a.data.charge.id).toBe('a1')
  expect(b.data.charge.id).toBe('b1')
})
