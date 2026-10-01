'use strict'
// An ordinary component test of the service: nothing in it knows about Kronikol or OpenTelemetry, but for the one
// line that hands the harness's MSW server to the capture (which a shared MSW setup file would hold).
const { setupServer } = require('msw/node')
const { http, HttpResponse } = require('msw')
const { captureMsw } = require('../kronikol/capture')
const { startRiskFake } = require('../../harness/fakes')
const { buildApp } = require('../../src/app')

const msw = setupServer(http.get('https://psp.example.test/charges/:id', ({ params }) => HttpResponse.json({ id: params.id, amount: 1250, status: 'SETTLED' })))
captureMsw(msw, host => ({ 'psp.example.test': 'PSP' })[host] ?? 'Risk')
let risk
let app
beforeAll(async () => { risk = await startRiskFake(); msw.listen({ onUnhandledRequest: 'bypass' }) })
afterAll(async () => { msw.close(); risk.close() })

const charge = id => app.inject({ method: 'POST', url: '/graphql', payload: { query: `query Charge { charge(id: "${id}") { id amount fee status receipt { number } } }` } })

describe('charges', () => {
  beforeEach(async () => {
    app = buildApp()
    await app.ready()
    // A call made in setup: drawn in the Setup partition with --separate-setup.
    await app.inject({ method: 'POST', url: '/graphql', payload: { query: '{ __typename }' } })
  })
  afterEach(() => app.close())

  test('a card is charged', async () => {
    const res = await charge('c2')
    expect(JSON.parse(res.body).data.charge.status).toBe('SETTLED')
  })

  let tries = 0
  jest.retryTimes(1)
  test('a flaky charge passes on its retry', async () => {
    tries++
    const res = await charge('c3')
    expect(tries).toBe(2)
    expect(JSON.parse(res.body).data.charge.receipt.number).toBe('R-c3')
  })
})
