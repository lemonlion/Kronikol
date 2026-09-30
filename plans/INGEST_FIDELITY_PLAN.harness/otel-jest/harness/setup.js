'use strict'
// V4's setupFiles entry: the realistic form. The test files contain no tracing code at all; this runs
// in each test file's module registry before the file loads, so the patches land on the instances the
// file (and the service it loads) will use.
const { registerInstrumentations } = require('@opentelemetry/instrumentation')
const { UndiciInstrumentation } = require('@opentelemetry/instrumentation-undici')
const { GraphQLInstrumentation } = require('@opentelemetry/instrumentation-graphql')
const { FastifyOtelInstrumentation } = require('@fastify/otel')
const { testIds, startTracing, patchThroughJest } = require('./tracing')

const { provider, exporter } = startTracing()
const fastifyOtel = new FastifyOtelInstrumentation({ registerOnInitialization: true })
registerInstrumentations({
  tracerProvider: provider,
  instrumentations: [new UndiciInstrumentation(), fastifyOtel]
})
patchThroughJest(new GraphQLInstrumentation({ mergeItems: true }), require, provider)
globalThis.__kronikolSpike = { exporter, testIds, fastifyOtel }
