'use strict'
// V2: what a harness can do without touching the service: @fastify/otel registered by Fastify's own
// initialization diagnostics channel, the GraphQL instrumentation's patches applied through Jest's
// require, undici (global fetch) through its diagnostics channels, and core http through require.
const fs = require('node:fs')
const { registerInstrumentations } = require('@opentelemetry/instrumentation')
const { HttpInstrumentation } = require('@opentelemetry/instrumentation-http')
const { UndiciInstrumentation } = require('@opentelemetry/instrumentation-undici')
const { GraphQLInstrumentation } = require('@opentelemetry/instrumentation-graphql')
const { FastifyOtelInstrumentation } = require('@fastify/otel')
const { JsonTraceSerializer } = require('@opentelemetry/otlp-transformer')
const { testIds, startTracing, patchThroughJest, summarise } = require('../harness/tracing')

const { provider, exporter } = startTracing()
// Fastify publishes its initialization on a process-wide diagnostics channel, so a subscription left by
// one test file registers a second plugin on the next file's instances in the same worker: disable it.
const fastifyOtel = new FastifyOtelInstrumentation({ registerOnInitialization: true })
afterAll(() => fastifyOtel.disable())
registerInstrumentations({
  tracerProvider: provider,
  instrumentations: [new HttpInstrumentation(), new UndiciInstrumentation(), fastifyOtel]
})
const patchedFiles = patchThroughJest(new GraphQLInstrumentation({ mergeItems: true }), require, provider)

const { startRiskFake, startMsw } = require('../harness/fakes')
const { addCaptureHook, chargeQuery } = require('../harness/drive')
const { buildApp } = require('../src/app')

test('V2: the harness-only setup under Jest', async () => {
  const seen = []
  const risk = await startRiskFake()
  const msw = startMsw(testIds, seen)
  const app = buildApp()
  addCaptureHook(app, testIds, seen)
  await app.ready()
  const body = await testIds.run({ testId: 'T-V2' }, () => chargeQuery(app, 'c2'))
  await app.close(); msw.close(); risk.close()
  const finished = exporter.getFinishedSpans()
  const spans = summarise(finished)
  fs.writeFileSync(`${__dirname}/../results/v2-harness.json`, JSON.stringify({ patchedFiles, body, seen, spans }, null, 2))
  // The same spans as one OTLP/JSON ExportTraceServiceRequest per line, the OpenTelemetry file-exporter shape.
  fs.writeFileSync(`${__dirname}/../results/v2-spans.otlp.jsonl`, Buffer.from(JsonTraceSerializer.serializeRequest(finished)).toString('utf8') + '\n')
  expect(body.data.charge.receipt.number).toBe('R-c2')
})
