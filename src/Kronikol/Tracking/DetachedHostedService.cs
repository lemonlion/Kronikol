using Microsoft.Extensions.Hosting;

namespace Kronikol.Tracking;

/// <summary>
/// Runs an <see cref="IHostedService"/>'s lifetime work in a detached flow (<see cref="TestIdentityScope.Detach"/>).
/// </summary>
/// <remarks>
/// A host built inside a test inherits the test's execution context, and every hosted service that host
/// starts inherits it in turn: a background loop would resolve the test's identity for every call it
/// makes, for as long as the host lives. Starting it detached severs that inheritance for the loop and
/// everything the loop starts, while a message that names its scenario (<see cref="TestIdentityScope.SetFromMessage"/>,
/// <see cref="TestIdentityScope.Begin"/>) is still attributed to it.
/// See <c>ServiceCollectionHelper.DetachHostedServicesFromTestIdentity</c>.
/// </remarks>
internal sealed class DetachedHostedService(IHostedService inner) : IHostedLifecycleService
{
    /// <summary>The service being wrapped.</summary>
    public IHostedService Inner { get; } = inner;

    public Task StartAsync(CancellationToken cancellationToken) => Detached(() => Inner.StartAsync(cancellationToken));

    public Task StopAsync(CancellationToken cancellationToken) => Detached(() => Inner.StopAsync(cancellationToken));

    public Task StartingAsync(CancellationToken cancellationToken) =>
        Inner is IHostedLifecycleService lifecycle ? Detached(() => lifecycle.StartingAsync(cancellationToken)) : Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) =>
        Inner is IHostedLifecycleService lifecycle ? Detached(() => lifecycle.StartedAsync(cancellationToken)) : Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) =>
        Inner is IHostedLifecycleService lifecycle ? Detached(() => lifecycle.StoppingAsync(cancellationToken)) : Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) =>
        Inner is IHostedLifecycleService lifecycle ? Detached(() => lifecycle.StoppedAsync(cancellationToken)) : Task.CompletedTask;

    /// <summary>
    /// Starts <paramref name="work"/> inside a detached scope. The scope is left as soon as the work has
    /// started, which is enough: everything the work does after its first await was captured with the
    /// detachment, and the caller's own flow gets its attachment back.
    /// </summary>
    private static Task Detached(Func<Task> work)
    {
        using (TestIdentityScope.Detach())
            return work();
    }
}
