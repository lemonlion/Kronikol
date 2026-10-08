using Kronikol.NUnit4;
using NUnit.Framework;

namespace Probe133;

[SetUpFixture]
public class Run : DiagrammedTestRun
{
    [OneTimeSetUp] public void Start() { Wire(); Probe.Record("[SetUpFixture] OneTimeSetUp (assembly level, after DiagrammedTestRun.Setup)"); }
    [OneTimeTearDown] public void End() { Probe.Record("[SetUpFixture] OneTimeTearDown (assembly level)"); Probe.Flush("probe-nunit.txt"); }

    public static void Wire()
    {
        Setup();
        Probe.Fetcher = CurrentTestInfo.Fetcher;
        Probe.InsertPlantUml = s => TrackingDiagramOverride.InsertPlantUml(s);
        Probe.Context = () =>
        {
            var t = TestContext.CurrentContext.Test;
            return $"Test.ID={t.ID} Name={t.Name} MethodName={t.MethodName ?? "null"} ClassName={t.ClassName ?? "null"}";
        };
    }
}

[TestFixture]
public class NUnitProbe : DiagrammedComponentTest
{
    public NUnitProbe() => Probe.Record("fixture constructor");

    [OneTimeSetUp] public void FixtureSetUp() => Probe.Record("[OneTimeSetUp] (fixture)");

    [SetUp] public void OwnSetUp() => Probe.Record("[SetUp] (the test's own, after the base's)");

    [Test]
    public void Test_one()
    {
        Probe.Record("Test_one body");
        Probe.ProbeOtherFlows("Test_one");
    }

    [Test]
    public void Test_two() => Probe.Record("Test_two body");

    [TearDown] public void OwnTearDown() => Probe.Record("[TearDown] (the test's own, before the base's)");

    [OneTimeTearDown] public void FixtureTearDown() => Probe.Record("[OneTimeTearDown] (fixture)");
}
