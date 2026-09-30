'use strict'
// V4: an ordinary component test. No tracing code here; harness/setup.js (Jest setupFiles) did it.
const fs = require('node:fs')
const { summarise } = require('../harness/tracing')
const { startRiskFake, startMsw } = require('../harness/fakes')
const { addCaptureHook, chargeQuery } = require('../harness/drive')
const { buildApp } = require('../src/app')

test('V4: setupFiles only', async () => {
  const { exporter, testIds } = globalThis.__kronikolSpike
  const seen = []
  const risk = await startRiskFake()
  const msw = startMsw(testIds, seen)
  const app = buildApp()
  addCaptureHook(app, testIds, seen)
  await app.ready()
  const body = await testIds.run({ testId: 'T-V4' }, () => chargeQuery(app, 'c4'))
  await app.close(); msw.close(); risk.close()
  const spans = summarise(exporter.getFinishedSpans())
  fs.writeFileSync(`${__dirname}/../results/v4-setupfiles.json`, JSON.stringify({ body, seen, spans }, null, 2))
  expect(body.data.charge.status).toBe('SETTLED')
})
