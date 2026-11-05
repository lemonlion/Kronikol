using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

public class TestPhaseContextTests
{
    public TestPhaseContextTests()
    {
        TestPhaseContext.Reset();
    }

    [Fact]
    public void Current_defaults_to_Unknown()
    {
        Assert.Equal(TestPhase.Unknown, TestPhaseContext.Current);
    }

    [Fact]
    public void Current_can_be_set_to_Setup()
    {
        TestPhaseContext.Current = TestPhase.Setup;

        Assert.Equal(TestPhase.Setup, TestPhaseContext.Current);
    }

    [Fact]
    public void Current_can_be_set_to_Action()
    {
        TestPhaseContext.Current = TestPhase.Action;

        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);
    }

    [Fact]
    public void Reset_sets_back_to_Unknown()
    {
        TestPhaseContext.Current = TestPhase.Action;

        TestPhaseContext.Reset();

        Assert.Equal(TestPhase.Unknown, TestPhaseContext.Current);
    }

    [Fact]
    public async Task Current_is_isolated_across_async_contexts()
    {
        TestPhaseContext.Current = TestPhase.Setup;

        var innerPhase = TestPhase.Unknown;
        await Task.Run(() =>
        {
            innerPhase = TestPhaseContext.Current;
            TestPhaseContext.Current = TestPhase.Action;
        }, TestContext.Current.CancellationToken);

        // Inner task inherits parent's value
        Assert.Equal(TestPhase.Setup, innerPhase);
        // Parent is not affected by child's change
        Assert.Equal(TestPhase.Setup, TestPhaseContext.Current);
    }

    // plans/STEP_PHASE_PLAN.md 5.2: this type does not change, so a flow goes on reading what it holds.
    [Fact]
    public async Task A_flow_forked_before_a_When_still_reads_what_it_held()
    {
        var id = $"forked-before-a-when-{Guid.NewGuid():N}";
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TestPhaseContext.Current = TestPhase.Setup;
        var forked = Task.Run(async () =>
        {
            await gate.Task;
            return TestPhaseContext.Current;
        }, TestContext.Current.CancellationToken);

        StepCollector.StartStep(id, "When", "the act", null, null);
        gate.SetResult();
        var read = await forked;
        StepCollector.CompleteStep(id, passed: true);

        Assert.Equal(TestPhase.Setup, read);
        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);
    }
}
