using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

[Collection("DiagramsFetcher")]
public class ProcessingCorrelationTests
{
    public ProcessingCorrelationTests()
    {
        TestCorrelationStore.Clear();
        TestIdentityScope.Reset();
    }

    private record WorkItem(string Id, string Payload);

    [Fact]
    public async Task Wrap_Sets_TestIdentityScope_For_Correlated_Item()
    {
        TestCorrelationStore.Correlate("custom:processor:item-1", "Test A", "id-a");

        (string Name, string Id)? captured = null;
        var wrapped = ProcessingCorrelation.Wrap<WorkItem>(
            (item, ct) =>
            {
                captured = TestIdentityScope.Current;
                return Task.CompletedTask;
            },
            item => CorrelationKeys.Custom("custom", "processor", item.Id));

        await wrapped(new WorkItem("item-1", "data"), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("Test A", captured.Value.Name);
    }

    [Fact]
    public async Task Wrap_NoOp_When_Key_Not_Found()
    {
        (string Name, string Id)? captured = null;
        var wrapped = ProcessingCorrelation.Wrap<WorkItem>(
            (item, ct) =>
            {
                captured = TestIdentityScope.Current;
                return Task.CompletedTask;
            },
            item => CorrelationKeys.Custom("custom", "processor", item.Id));

        await wrapped(new WorkItem("unknown", "data"), CancellationToken.None);

        Assert.Null(captured);
    }

    [Fact]
    public void WrapSync_Sets_TestIdentityScope()
    {
        TestCorrelationStore.Correlate("custom:processor:item-1", "Test B", "id-b");

        (string Name, string Id)? captured = null;
        var wrapped = ProcessingCorrelation.WrapSync<WorkItem>(
            item => captured = TestIdentityScope.Current,
            item => CorrelationKeys.Custom("custom", "processor", item.Id));

        wrapped(new WorkItem("item-1", "data"));

        Assert.NotNull(captured);
        Assert.Equal("Test B", captured.Value.Name);
    }

    [Fact]
    public async Task WrapBatch_Uses_First_Correlatable_Item()
    {
        TestCorrelationStore.Correlate("custom:processor:item-2", "Test C", "id-c");

        (string Name, string Id)? captured = null;
        var wrapped = ProcessingCorrelation.WrapBatch<WorkItem>(
            (items, ct) =>
            {
                captured = TestIdentityScope.Current;
                return Task.CompletedTask;
            },
            item => CorrelationKeys.Custom("custom", "processor", item.Id));

        await wrapped([new("unknown", "x"), new("item-2", "y")], CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("Test C", captured.Value.Name);
    }

    [Fact]
    public async Task Wrap_Restores_Identity_After_Dispose()
    {
        TestCorrelationStore.Correlate("custom:processor:item-1", "Test A", "id-a");

        var wrapped = ProcessingCorrelation.Wrap<WorkItem>(
            (_, _) => Task.CompletedTask,
            item => CorrelationKeys.Custom("custom", "processor", item.Id));

        await wrapped(new WorkItem("item-1", "data"), CancellationToken.None);

        Assert.Null(TestIdentityScope.Current);
    }

    [Fact]
    public async Task Wrap_Disposes_Even_On_Exception()
    {
        TestCorrelationStore.Correlate("custom:processor:item-1", "Test A", "id-a");

        var wrapped = ProcessingCorrelation.Wrap<WorkItem>(
            (_, _) => throw new InvalidOperationException("boom"),
            item => CorrelationKeys.Custom("custom", "processor", item.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => wrapped(new WorkItem("item-1", "data"), CancellationToken.None));

        Assert.Null(TestIdentityScope.Current);
    }
}
