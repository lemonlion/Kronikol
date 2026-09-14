import { enrichReportWithInsights } from "./run-insights.ts";

const mk = (i: number, tests: any[]) => ({
  reportFormat: "CTRF", specVersion: "1.0.0",
  results: {
    tool: { name: "kronikol" },
    summary: { tests: tests.length, passed: tests.length, failed: 0, pending: 0, skipped: 0, other: 0,
               start: 1700000000000 + i * 1000, stop: 1700000000000 + i * 1000 + 500 },
    tests,
  },
  timestamp: new Date(1700000000000 + i * 1000).toISOString(),
});

const kron = (i: number) => mk(i, [{
  name: "Pay with an expired card", status: "passed", duration: 1200,
  flaky: true, extra: { kronikolDrift: "shape changed on 2026-09-10", stableId: "a1b2c3d4" },
  insights: { flakyRate: { current: 0.42, baseline: 0, change: 0.42 } },
}]);

const retry = (i: number) => mk(i, [{
  name: "Pay with an expired card", status: "passed", duration: 1200, retries: 2,
}]);

for (const [label, gen] of [["KRONIKOL  flaky:true, retries:0", kron], ["RETRY     retries:2", retry]] as const) {
  const prev = [1, 2, 3, 4].map(gen);
  const out: any = enrichReportWithInsights(gen(5), prev);
  const t = out.results.tests[0];
  console.log(`\n===== ${label} =====`);
  console.log("  run  flakyRate.current :", out.insights?.flakyRate?.current);
  console.log("  test flakyRate.current :", t.insights?.flakyRate?.current);
  console.log("  test insights.extra    :", JSON.stringify(t.insights?.extra));
  console.log("  producer test.extra    :", JSON.stringify(t.extra));
  console.log("  producer test.flaky    :", t.flaky);
}
