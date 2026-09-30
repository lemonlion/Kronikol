'use strict'
// MSW fakes for the payment provider, a real local HTTP fake for the risk service (MSW passes it
// through), and an MSW lifecycle listener that records what the harness can see when an outbound call
// starts: the active span and the test id in the harness's AsyncLocalStorage.
const http = require('node:http')
const { setupServer } = require('msw/node')
const { http: mswHttp, HttpResponse } = require('msw')
const { trace } = require('@opentelemetry/api')

function startRiskFake () {
  const server = http.createServer((req, res) => {
    res.setHeader('content-type', 'application/json')
    res.end(JSON.stringify({ score: 12 }))
  })
  return new Promise(resolve => server.listen(0, '127.0.0.1', () => {
    process.env.RISK_URL = `http://127.0.0.1:${server.address().port}`
    resolve(server)
  }))
}

function startMsw (testIds, seen) {
  const server = setupServer(
    mswHttp.get('https://psp.example.test/charges/:id', ({ params }) =>
      HttpResponse.json({ id: params.id, amount: 1250, status: 'SETTLED' }))
  )
  server.events.on('request:start', ({ request }) => {
    const span = trace.getActiveSpan()
    seen.push({
      at: 'msw request:start',
      url: request.url,
      activeTraceId: span?.spanContext().traceId ?? null,
      activeSpanName: span?.name ?? null,
      testId: testIds.getStore()?.testId ?? null
    })
  })
  server.listen({ onUnhandledRequest: 'bypass' })
  return server
}

module.exports = { startRiskFake, startMsw }
