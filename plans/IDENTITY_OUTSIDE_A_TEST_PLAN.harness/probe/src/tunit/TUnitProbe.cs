using Kronikol.TUnit;
using TUnit.Core;

[assembly: NotInParallel]

namespace Probe133;

public class Run : DiagrammedTestRun
{
    [Before(Assembly)]
    public static Task Start()
    {
        Setup();
        Probe.Fetcher = CurrentTestInfo.Fetcher;
        Probe.InsertPlantUml = s => TrackingDiagramOverride.InsertPlantUml(s);
        Probe.Context = () => $"TestContext.Current={(TestContext.Current is { } c ? c.Id : "null")}";
        Probe.Record("[Before(Assembly)]");
        return Task.CompletedTask;
    }

    [After(Assembly)]
    public static Task End()
    {
        Probe.Record("[After(Assembly)]");
        Probe.Flush("probe-tunit.txt");
        return Task.CompletedTask;
    }
}

public class TUnitProbe : DiagrammedComponentTest
{
    public TUnitProbe() => Probe.Record("test class constructor");

    [Before(Class)] public static Task ClassStart() { Probe.Record("[Before(Class)]"); return Task.CompletedTask; }

    [Before(Test)] public Task TestStart() { Probe.Record("[Before(Test)]"); return Task.CompletedTask; }

    [Test]
    public async Task Test_one()
    {
        Probe.Record("Test_one body");
        Probe.ProbeOtherFlows("Test_one");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Test_two()
    {
        Probe.Record("Test_two body");
        await Task.CompletedTask;
    }

    [After(Test)] public Task TestEnd() { Probe.Record("[After(Test)]"); return Task.CompletedTask; }

    [After(Class)] public static Task ClassEnd() { Probe.Record("[After(Class)]"); return Task.CompletedTask; }
}
