using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using PhaseProbe;
using Xunit;

namespace PhaseProbe.X3;

/// <summary>xRetry reruns the same test; what an attempt that failed leaves behind for the next.</summary>
public class Retries : Steps
{
    private static int _x01, _x02, _x03;

    [xRetry.v3.RetryFact(3)]
    public async Task X01_retry_fails_after_when()
    {
        var n = Interlocked.Increment(ref _x01);
        Note($"attempt {n} id {Id}");
        await GivenCall($"x01-a{n}-g");
        await WhenCall($"x01-a{n}-w");
        Assert.True(n > 1, "first attempt fails");
        await ThenCall($"x01-a{n}-t");
    }

    [xRetry.v3.RetryFact(3)]
    public async Task X02_retry_fails_in_given()
    {
        var n = Interlocked.Increment(ref _x02);
        Note($"attempt {n} id {Id}");
        await GivenCall($"x02-a{n}-g");
        Assert.True(n > 1, "first attempt fails");
        await WhenCall($"x02-a{n}-w");
    }

    [xRetry.v3.RetryFact(3)]
    public async Task X03_retry_with_explicit_boundary()
    {
        var n = Interlocked.Increment(ref _x03);
        Note($"attempt {n} id {Id}");
        await C.Make($"x03-a{n}-setup");
        TrackingDiagramOverride.StartAction();
        await C.Make($"x03-a{n}-act");
        Assert.True(n > 1, "first attempt fails");
    }
}
