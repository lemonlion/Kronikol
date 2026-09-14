import { enrichReportWithInsights } from "./run-insights.ts";
import { prefixTestNames, shouldPrefixTestNames } from "./prefix.ts";

const mk = (i: number, status: string) => ({
  reportFormat: "CTRF", specVersion: "1.0.0",
  results: { tool: { name: "t" },
    summary: { tests: 1, passed: 0, failed: 0, pending: 0, skipped: 0, other: 0,
               start: 1700000000000 + i * 1000, stop: 1700000000000 + i * 1000 + 500 },
    tests: [{ name: "Pay with an expired card", suite: ["Checkout"], status, duration: 1200 }] },
  timestamp: new Date(1700000000000 + i * 1000).toISOString(),
});
const hist = [1, 2, 3, 4].map(i => mk(i, i === 2 ? "failed" : "passed"));

console.log("shouldPrefixTestNames({useSuiteName:true})  =", shouldPrefixTestNames({ useSuiteName: true }));
console.log("shouldPrefixTestNames({useSuiteName:false}) =", shouldPrefixTestNames({ useSuiteName: false }));

const plain: any = enrichReportWithInsights(mk(5, "failed"), hist);
console.log("\nwithout prefixing: name=%j failRate=%s runs=%s",
  plain.results.tests[0].name, plain.results.tests[0].insights?.failRate?.current,
  plain.results.tests[0].insights?.extra?.appearsInRuns);

// the REAL transform, applied to the current run only — exactly what toggling the input does
const prefixed: any = enrichReportWithInsights(prefixTestNames(mk(5, "failed")), hist);
console.log("after prefixTestNames:  name=%j failRate=%s runs=%s",
  prefixed.results.tests[0].name, prefixed.results.tests[0].insights?.failRate?.current,
  prefixed.results.tests[0].insights?.extra?.appearsInRuns);
