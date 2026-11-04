using Kronikol.Tracking;
using Shouldly;

[assembly: TrackSteps]
[assembly: TrackAssertions]

public class Shop
{
    [Step] public Task<InvalidOperationException> Returned() { return Should.ThrowAsync<InvalidOperationException>(() => Task.FromException(new InvalidOperationException("x"))); }
    [Step] public Task<InvalidOperationException> AsAnExpression() => Should.ThrowAsync<InvalidOperationException>(() => Task.FromException(new InvalidOperationException("x")));
    [Step] public async Task Awaited() { await Should.ThrowAsync<InvalidOperationException>(() => Task.FromException(new InvalidOperationException("x"))); }
}
