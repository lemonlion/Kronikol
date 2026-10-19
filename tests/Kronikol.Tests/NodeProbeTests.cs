namespace Kronikol.Tests;

/// <summary>
/// Whether the machine's <c>node</c> runs decides whether some forty tests run or skip, once for the whole run. The probe
/// waited five seconds for <c>node --version</c> and read the exit code of a process still running, which threw, which
/// it took for "not available": on a loaded machine every Node test in the run skipped without a failure (2026-10-19,
/// 40 of the core suite's tests in one run).
/// </summary>
public class NodeProbeTests
{
    [Fact]
    public void A_node_slower_than_five_seconds_to_answer_still_answers()
    {
        Assert.SkipWhen(!NodeProbe.IsAvailable, "Node.js not available on PATH");

        Assert.True(NodeProbe.Answers("-e \"setTimeout(() => process.exit(0), 6000)\"", TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void A_node_that_never_answers_is_stopped_and_does_not_count()
    {
        Assert.SkipWhen(!NodeProbe.IsAvailable, "Node.js not available on PATH");

        var started = DateTime.UtcNow;
        Assert.False(NodeProbe.Answers("-e \"setTimeout(() => {}, 120000)\"", TimeSpan.FromSeconds(2)));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(30), "the probe waited for the process it should have stopped");
    }

    [Fact]
    public void A_node_that_fails_does_not_count()
    {
        Assert.SkipWhen(!NodeProbe.IsAvailable, "Node.js not available on PATH");

        Assert.False(NodeProbe.Answers("-e \"process.exit(3)\"", TimeSpan.FromSeconds(60)));
    }
}
