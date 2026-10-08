using Kronikol.xUnit3;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: AssemblyFixture(typeof(Probe133.AssemblyProbe))]

namespace Probe133;

public class AssemblyProbe : IAsyncLifetime
{
    public AssemblyProbe()
    {
        Probe.Fetcher = CurrentTestInfo.Fetcher;
        Probe.InsertPlantUml = s => TrackingDiagramOverride.InsertPlantUml(s);
        Probe.Context = () => $"TestContext.Current.Test={TestContext.Current.Test?.TestDisplayName ?? "null"} Stage={TestContext.Current.PipelineStage}";
        Probe.Record("assembly fixture constructor");
    }
    public ValueTask InitializeAsync() { Probe.Record("assembly fixture InitializeAsync"); return default; }
    public ValueTask DisposeAsync() { Probe.Record("assembly fixture DisposeAsync"); Probe.Flush("probe-xunit3.txt"); return default; }
}

public class ClassFixture : IAsyncLifetime
{
    public ClassFixture() => Probe.Record("class fixture constructor");
    public ValueTask InitializeAsync() { Probe.Record("class fixture InitializeAsync"); return default; }
    public ValueTask DisposeAsync() { Probe.Record("class fixture DisposeAsync"); return default; }
}

public class XUnit3Probe : DiagrammedComponentTest, IClassFixture<ClassFixture>, IAsyncLifetime
{
    public XUnit3Probe(ClassFixture _) => Probe.Record("test class constructor");
    public ValueTask InitializeAsync() { Probe.Record("test class InitializeAsync"); return default; }

    [Fact]
    public void Test_one()
    {
        Probe.Record("Test_one body");
        Probe.ProbeOtherFlows("Test_one");
    }

    public ValueTask DisposeAsync() { Probe.Record("test class DisposeAsync"); return default; }
}
