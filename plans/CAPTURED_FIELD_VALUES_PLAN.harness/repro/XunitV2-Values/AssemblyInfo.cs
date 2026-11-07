using Kronikol.Tracking;
using Kronikol.xUnit2;
using Xunit;

[assembly: TestTracking]
[assembly: TrackAssertions]
// Not in the issue: under xUnit v2 the report (and the notes this harness reads from it) is written only by
// Kronikol's test framework.
[assembly: TestFramework("Kronikol.xUnit2.ReportingTestFramework", "Kronikol.xUnit2")]
