'use strict'
// The harness side: an in-memory tracer provider, a span processor that stamps the running test's id
// (read from the harness's AsyncLocalStorage) on every span it starts, and a way to apply an
// OpenTelemetry instrumentation's module patches to modules loaded through Jest's own module registry.
const { AsyncLocalStorage } = require('node:async_hooks')
const { trace, context } = require('@opentelemetry/api')
const { BasicTracerProvider, InMemorySpanExporter, SimpleSpanProcessor } = require('@opentelemetry/sdk-trace-base')
const { AsyncLocalStorageContextManager } = require('@opentelemetry/context-async-hooks')

const testIds = new AsyncLocalStorage()

class TestIdSpanProcessor {
  onStart (span) {
    const store = testIds.getStore()
    if (store) span.setAttribute('kronikol.test.id', store.testId)
  }
  onEnd () {}
  forceFlush () { return Promise.resolve() }
  shutdown () { return Promise.resolve() }
}

function startTracing () {
  const exporter = new InMemorySpanExporter()
  const provider = new BasicTracerProvider({ spanProcessors: [new TestIdSpanProcessor(), new SimpleSpanProcessor(exporter)] })
  const contextManager = new AsyncLocalStorageContextManager()
  contextManager.enable()
  context.setGlobalContextManager(contextManager)
  trace.setGlobalTracerProvider(provider)
  return { provider, exporter }
}

// OpenTelemetry's auto-instrumentation hooks Node's require (require-in-the-middle). Jest loads modules
// through its own registry, so the hook never sees them. getModuleDefinitions() (there for bundlers)
// hands over the same patch functions; applying them to what Jest's require returns patches the module
// instances the test and the service actually use. It must run before the service's modules load.
function patchThroughJest (instrumentation, requireFn, provider) {
  instrumentation.setTracerProvider(provider)
  const patched = []
  for (const definition of instrumentation.getModuleDefinitions()) {
    const version = requireFn(`${definition.name}/package.json`).version
    if (definition.patch) definition.patch(requireFn(definition.name), version)
    for (const file of definition.files ?? []) {
      file.patch(requireFn(file.name), version)
      patched.push(file.name)
    }
  }
  return patched
}

function summarise (spans) {
  return spans.map(s => ({
    name: s.name,
    scope: s.instrumentationScope?.name ?? s.instrumentationLibrary?.name,
    kind: s.kind,
    traceId: s.spanContext().traceId,
    spanId: s.spanContext().spanId,
    parentSpanId: s.parentSpanContext?.spanId ?? s.parentSpanId ?? null,
    testId: s.attributes['kronikol.test.id'] ?? null,
    durationMs: Math.round((s.duration[0] * 1e3 + s.duration[1] / 1e6) * 100) / 100
  }))
}

module.exports = { testIds, startTracing, patchThroughJest, summarise }
