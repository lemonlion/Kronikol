'use strict'
// The service under test, standing in for superpay-graphql: Fastify + Mercurius, one resolver that
// calls a payment provider (faked by MSW), a risk service (a real local fake, passed through) and an
// internal async function. Nothing in this file knows about OpenTelemetry or Kronikol: the spike's
// question is what a test harness alone can get out of it.
const Fastify = require('fastify')
const mercurius = require('mercurius')

const schema = `
  type Charge { id: ID!, amount: Int!, fee: Int!, status: String!, receipt: Receipt! }
  type Receipt { number: String! }
  type Query { charge(id: ID!): Charge }
`

async function computeFee (amount) {
  await new Promise(resolve => setTimeout(resolve, 5))
  return Math.round(amount * 0.029) + 30
}

const resolvers = {
  Query: {
    async charge (_, { id }) {
      const psp = await fetch(`https://psp.example.test/charges/${id}`)
      const body = await psp.json()
      const risk = await fetch(`${process.env.RISK_URL}/score/${id}`)
      const { score } = await risk.json()
      return { id, amount: body.amount, fee: await computeFee(body.amount), status: score > 50 ? 'REVIEW' : body.status }
    }
  },
  Charge: {
    async receipt (charge) {
      await new Promise(resolve => setTimeout(resolve, 2))
      return { number: `R-${charge.id}` }
    }
  }
}

function buildApp () {
  const app = Fastify()
  app.register(mercurius, { schema, resolvers })
  return app
}

module.exports = { buildApp }
