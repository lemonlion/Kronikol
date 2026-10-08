using Xunit;
using Xunit.Abstractions;

namespace Probe;

public partial class InitThrows(ITestOutputHelper output) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await Task.Yield();
        throw new InvalidOperationException("InitializeAsync throws on purpose");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public Task Never_runs() => ProbeHttp.GetAsync("/init/never", output);
}
