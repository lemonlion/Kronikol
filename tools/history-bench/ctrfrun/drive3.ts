import { enrichReportWithInsights } from "./run-insights.ts";
const mk = (i: number, name: string, status: string) => ({
  reportFormat: "CTRF", specVersion: "1.0.0",
  results: { tool: { name: "t" },
    summary: { tests: 1, passed: 0, failed: 0, pending: 0, skipped: 0, other: 0,
               start: 1700000000000 + i * 1000, stop: 1700000000000 + i * 1000 + 500 },
    tests: [{ name, status, duration: 1200 }] },
  timestamp: new Date(1700000000000 + i * 1000).toISOString(),
});
const show = (label: string, cur: any, prev: any[]) => {
  const t: any = enrichReportWithInsights(cur, prev).results.tests[0];
  console.log(label.padEnd(46),
    "failRate", String(t.insights?.failRate?.current).padEnd(8),
    "executedInRuns/appearsInRuns", t.insights?.executedInRuns ?? t.insights?.extra?.appearsInRuns);
};
const OLD = "Pay with an expired card", NEW = "Pay with a card that has expired";
// five runs of history, the test fails in two of them
const hist = (n: string) => [1,2,3,4].map(i => mk(i, n, i === 2 ? "failed" : "passed"));
show("history intact (same name)",        mk(5, OLD, "failed"), hist(OLD));
show("test RENAMED on the current run",   mk(5, NEW, "failed"), hist(OLD));
// what use-suite-name does: prefixTestNames rewrites name to "<suite> - <name>"
show("use-suite-name toggled ON this run", mk(5, "Checkout - " + OLD, "failed"), hist(OLD));
