using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using Xunit;

[assembly: TrackSteps]

namespace ProbeService;

public class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        XUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime, new ReportConfigurationOptions
        {
            ReportsFolderPath = Path.Combine(AppContext.BaseDirectory, "Reports"),
            InternalFlowTracking = false,
            GenerateComponentDiagram = false,
        });
    }
}

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class DiagrammedTestCollection : ICollectionFixture<TestRun> { }

public class Steps
{
    private static Task Call(string name) => Service.Client.GetAsync($"/call/{name}");

    [Step] public async Task The_parent() { await Call("own-before"); await Sub_a(); await Sub_b(); await Call("own-after"); }
    [Step] public Task Sub_a() => Call("sub-a");
    [Step] public Task Sub_b() => Call("sub-b");

    [Step] public async Task Both_at_once() => await Task.WhenAll(Left(), Right());
    [Step] public async Task Left() { await Task.Delay(1); await Call("left-slow"); }
    [Step] public async Task Right() { await Task.Delay(10); await Call("right"); }

    [Step] public async Task Parent_calls_while_a_child_runs() { var child = The_running_child(); await Task.Delay(20); await Call("parent-own"); await child; }
    [Step] public async Task The_running_child() { await Task.Delay(300); }

    [Step] public async Task Top_fast() { await Task.Delay(1); await Call("fast"); }
    [Step] public async Task Top_slow() { await Task.Delay(10); await Call("top-slow"); }
    [Step] public Task The_next() => Call("next");

    // A thread with no context, which takes the test's identity by hand, as a message handler in a fake does.
    [Step] public async Task Hands_work_to_a_thread_with_no_context(string name, string id)
    {
        var done = new TaskCompletionSource();
        ThreadPool.UnsafeQueueUserWorkItem(_ =>
        {
            using var scope = TestIdentityScope.Begin(name, id);
            On_that_thread().GetAwaiter().GetResult();
            done.SetResult();
        }, null);
        await done.Task;
    }
    [Step] public async Task On_that_thread() { await Task.Delay(5); await Call("on-the-thread"); }

    [Step] public Task Starts_work_and_moves_on() => Service.Client.GetAsync("/start/work");
    [Step] public Task The_later_step() => Task.Delay(300);
}

public class ServiceProbes : DiagrammedComponentTest
{
    private readonly Steps _s = new();

    [Fact] public Task S0_Sequential() => _s.The_parent();
    [Fact] public Task S1_Two_at_once() => _s.Both_at_once();
    [Fact] public Task S2_Parent_calls_while_a_child_runs() => _s.Parent_calls_while_a_child_runs();
    [Fact] public async Task S3_Two_top_level_steps_at_once() { await Task.WhenAll(_s.Top_fast(), _s.Top_slow()); await _s.The_next(); }
    [Fact] public async Task S4_Service_goes_on_after_it_answers() { await _s.Starts_work_and_moves_on(); await _s.The_later_step(); }
    [Fact] public async Task S5_A_step_on_a_thread_with_no_context()
    {
        var test = TestContext.Current.Test!;
        await _s.Hands_work_to_a_thread_with_no_context(test.TestDisplayName, test.UniqueID);
        await _s.The_next();
    }
}
