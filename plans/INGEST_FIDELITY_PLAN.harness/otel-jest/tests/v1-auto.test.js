'use strict'
// V1: the documented setup, as a harness would first try it: registerInstrumentations() with the
// auto-instrumentations, then load the service. Under Jest, which loads modules through its own registry.
const fs = require('node:fs')
const { registerInstrumentations } = require('@opentelemetry/instrumentation')
const { HttpInstrumentation } = require('@opentelemetry/instrumentation-http')
const { UndiciInstrumentation } = require('@opentelemetry/instrumentation-undici')
const { GraphQLInstrumentation } = require('@opentelemetry/instrumentation-graphql')
const { FastifyOtelInstrumentation } = require('@fastify/otel')
const { testIds, startTracing, summarise } = require('../harness/tracing')

const { provider, exporter } = startTracing()
const fastifyOtel = new FastifyOtelInstrumentation()
afterAll(() => fastifyOtel.disable())
registerInstrumentations({
  tracerProvider: provider,
  instrumentations: [new HttpInstrumentation(), new UndiciInstrumentation(), new GraphQLInstrumentation(), fastifyOtel]
})

const { startRiskFake, startMsw } = require('../harness/fakes')
const { addCaptureHook, chargeQuery } = require('../harness/drive')
const { buildApp } = require('../src/app')

test('V1: registerInstrumentations under Jest', async () => {
  const seen = []
  const risk = await startRiskFake()
  const msw = startMsw(testIds, seen)
  const app = buildApp()
  addCaptureHook(app, testIds, seen)
  await app.ready()
  const body = await testIds.run({ testId: 'T-V1' }, () => chargeQuery(app, 'c1'))
  await app.close(); msw.close(); risk.close()
  const spans = summarise(exporter.getFinishedSpans())
  fs.writeFileSync(`${__dirname}/../results/v1-auto.json`, JSON.stringify({ body, seen, spans }, null, 2))
  expect(body.data.charge.fee).toBe(66)
})
