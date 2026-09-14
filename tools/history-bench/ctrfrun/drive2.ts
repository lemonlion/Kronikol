import { enrichReportWithInsights } from "./run-insights.ts";
const mk = (i: number, tests: any[]) => ({
  reportFormat: "CTRF", specVersion: "1.0.0",
  results: { tool: { name: "t" },
    summary: { tests: 1, passed: 0, failed: 0, pending: 0, skipped: 0, other: 0,
               start: 1700000000000 + i * 1000, stop: 1700000000000 + i * 1000 + 500 }, tests },
  timestamp: new Date(1700000000000 + i * 1000).toISOString(),
});
// A GENUINELY FLAKY TEST: flips pass/fail across runs, never retried. Runs 1..6, fails on 2 and 5.
const flip = (setFlaky: boolean) => (i: number) => mk(i, [{
  name: "Pay with an expired card", status: (i === 2 || i === 5) ? "failed" : "passed",
  duration: 1200, ...(setFlaky ? { flaky: true } : {}),
}]);
for (const [label, setFlaky] of [["plain CTRF emitter (no flaky flag)", false],
                                 ["Kronikol sets flaky:true from flip rate", true]] as const) {
  const gen = flip(setFlaky);
  const out: any = enrichReportWithInsights(gen(6), [1,2,3,4,5].map(gen));
  const t = out.results.tests[0];
  console.log(`\n===== ${label} =====`);
  console.log("  flakyRate :", t.insights?.flakyRate?.current,
              "  failRate :", t.insights?.failRate?.current,
              "  passRate :", t.insights?.passRate?.current);
  console.log("  extra     :", JSON.stringify(t.insights?.extra));
}
