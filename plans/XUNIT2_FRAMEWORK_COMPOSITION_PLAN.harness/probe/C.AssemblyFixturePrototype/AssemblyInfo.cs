using Kronikol.xUnit2;
using Probe.Prototype;
using Xunit;

[assembly: TestFramework("Probe.Prototype.PrototypeFramework", "C.AssemblyFixturePrototype")]
// Order matters: xUnit runs assembly-level BeforeAfterTestAttributes in the order reflection returns
// them, and PrototypeTracking reads the identity TestTracking's Before has just set. The sink logs the
// order it saw (IBeforeTestStarting) for every test, so a run where it differs shows in asynclocal.tsv.
[assembly: TestTracking]
[assembly: PrototypeTracking]
