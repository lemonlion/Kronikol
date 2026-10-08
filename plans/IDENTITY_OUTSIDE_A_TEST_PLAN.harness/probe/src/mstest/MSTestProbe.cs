using Kronikol.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace Probe133;

[TestClass]
public class Run
{
    [AssemblyInitialize]
    public static void Start(TestContext _)
    {
        Probe.Fetcher = CurrentTestInfo.Fetcher;
        Probe.InsertPlantUml = s => TrackingDiagramOverride.InsertPlantUml(s);
        Probe.Context = () => "(MSTest: Kronikol's own AsyncLocal TestContext, read by the fetcher)";
        Probe.Record("[AssemblyInitialize]");
    }

    [AssemblyCleanup]
    public static void End()
    {
        Probe.Record("[AssemblyCleanup]");
        Probe.Flush("probe-mstest.txt");
    }
}

[TestClass]
public class MSTestProbe : DiagrammedComponentTest
{
    public MSTestProbe() => Probe.Record("test class constructor");

    [ClassInitialize] public static void ClassStart(TestContext _) => Probe.Record("[ClassInitialize]");

    [TestInitialize] public void OwnInit() => Probe.Record("[TestInitialize] (the test's own, after the base's)");

    [TestMethod]
    public void Test_one()
    {
        Probe.Record("Test_one body");
        Probe.ProbeOtherFlows("Test_one");
    }

    [TestMethod]
    public void Test_two() => Probe.Record("Test_two body");

    [TestCleanup] public void OwnCleanup() => Probe.Record("[TestCleanup] (the test's own, before the base's)");

    [ClassCleanup] public static void ClassEnd() => Probe.Record("[ClassCleanup]");
}
