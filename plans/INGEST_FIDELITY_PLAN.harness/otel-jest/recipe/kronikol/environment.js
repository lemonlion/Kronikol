'use strict'
// Jest environment: one tests NDJSON line per test start, its steps and its end. Steps are kept until the
// test ends so each carries its status: beforeEach is the Given step (setup), the test body the When step.
const { TestEnvironment } = require('jest-environment-node')
const crypto = require('node:crypto')
const fs = require('node:fs')
const path = require('node:path')

const dir = process.env.KRONIKOL_DIR ?? 'kronikol-capture'

function fullName (test) {
  const names = [test.name]
  for (let block = test.parent; block && block.name !== 'ROOT_DESCRIBE_BLOCK'; block = block.parent) names.unshift(block.name)
  return names.join(' › ')
}

function message (error) {
  const e = Array.isArray(error) ? error[0] : error
  return e?.stack ?? e?.message ?? String(e)
}

class KronikolEnvironment extends TestEnvironment {
  constructor (config, context) {
    super(config, context)
    this.file = path.relative(process.cwd(), context.testPath).split(path.sep).join('/')
    this.steps = []
    fs.mkdirSync(dir, { recursive: true })
  }

  write (record) {
    // One line per append: lines this short are written whole even with every worker appending to the file.
    fs.appendFileSync(path.join(dir, 'tests.ndjson'), JSON.stringify(record) + '\n')
  }

  step (keyword, text) {
    this.steps.push({ keyword, text, started: Date.now(), timestamp: new Date().toISOString() })
  }

  async handleTestEvent (event, state) {
    const test = event.test ?? state.currentlyRunningTest
    if (!test) return
    const name = fullName(test)
    const testId = crypto.createHash('sha256').update(`${this.file}|${name}`).digest('hex').slice(0, 32)
    const attempt = test.invocations
    switch (event.name) {
      case 'test_start':
        this.steps = []
        // The capture points stamp every record with it; the Fastify hook binds it to each request.
        this.global.__kronikolTestId = testId
        this.global.__kronikolAttempt = attempt
        this.write({ event: 'start', testId, testName: name, feature: this.file, sourceFile: this.file, attempt, timestamp: new Date().toISOString() })
        break
      case 'hook_start':
        // Every beforeEach hook of the test is the one Given step, timed from the first.
        if (event.hook.type === 'beforeEach' && this.steps.length === 0) this.step('Given', 'the test is set up')
        break
      case 'test_fn_start':
        this.step('When', 'the test runs')
        break
      case 'test_done': {
        const failed = test.errors.length > 0
        this.steps.forEach((step, i) => {
          const last = i === this.steps.length - 1
          const next = this.steps[i + 1]
          this.write({
            event: 'step', testId, keyword: step.keyword, text: step.text, attempt, timestamp: step.timestamp,
            status: failed && last ? 'failed' : 'passed',
            durationMs: (next?.started ?? Date.now()) - step.started,
            error: failed && last ? message(test.errors[0]) : undefined
          })
        })
        this.write({
          event: 'end', testId, attempt, timestamp: new Date().toISOString(),
          status: failed ? 'failed' : 'passed', durationMs: test.duration,
          error: failed ? message(test.errors[0]) : undefined
        })
        this.global.__kronikolTestId = undefined
        this.global.__kronikolAttempt = undefined
        break
      }
    }
  }
}

module.exports = KronikolEnvironment
