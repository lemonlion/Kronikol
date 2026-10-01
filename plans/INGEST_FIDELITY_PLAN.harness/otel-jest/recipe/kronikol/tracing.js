'use strict'
// Spans: an in-memory tracer provider and the instrumentations, applied the way Jest needs. Loaded from a
// setupFiles entry, so it runs in each test file's module registry before the file and the service load.
const { trace, context } = require('@opentelemetry/api')
const { AsyncLocalStorageContextManager } = require('@opentelemetry/context-async-hooks')
const { BasicTracerProvider, InMemorySpanExporter, SimpleSpanProcessor } = require('@opentelemetry/sdk-trace-base')
const { registerInstrumentations } = require('@opentelemetry/instrumentation')
const { UndiciInstrumentation } = require('@opentelemetry/instrumentation-undici')
const { GraphQLInstrumentation } = require('@opentelemetry/instrumentation-graphql')
const { FastifyOtelInstrumentation } = require('@fastify/otel')

// Spans need no test id: a call carries the trace it was made in, and that joins them.
const exporter = new InMemorySpanExporter()
const provider = new BasicTracerProvider({ spanProcessors: [new SimpleSpanProcessor(exporter)] })
const contextManager = new AsyncLocalStorageContextManager()
contextManager.enable()
context.setGlobalContextManager(contextManager)
trace.setGlobalTracerProvider(provider)

// Fastify announces each new instance on a diagnostics channel; registerOnInitialization adds the plugin to it.
// The channel is process-wide, so the subscription is undone after each test file (after-env.js).
const fastifyOtel = new FastifyOtelInstrumentation({ registerOnInitialization: true })
// undici (global fetch) reports on diagnostics channels too, which Jest's module registry does not hide.
registerInstrumentations({ tracerProvider: provider, instrumentations: [new UndiciInstrumentation(), fastifyOtel] })

// The GraphQL instrumentation hooks Node's require, which Jest bypasses: apply its patches to the modules
// Jest's own require returns, before the service is loaded (Mercurius takes graphql's functions when required).
const graphql = new GraphQLInstrumentation({ mergeItems: true })
graphql.setTracerProvider(provider)
for (const definition of graphql.getModuleDefinitions()) {
  const version = require(`${definition.name}/package.json`).version
  if (definition.patch) definition.patch(require(definition.name), version)
  for (const file of definition.files ?? []) file.patch(require(file.name), version)
}

module.exports = { exporter, fastifyOtel }
