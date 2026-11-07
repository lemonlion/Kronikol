using Kronikol.Extensions.DispatchProxy;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;
using Microsoft.Extensions.DependencyInjection;

namespace Kronikol.Tests.DispatchProxy;

/// <summary>
/// A tracked service's member that throws (plans/FAILED_CALLS_PLAN.md §5.1, Q2, F17): it is answered with the exception's type and
/// why, where it was a 500 no service sent, and the caller gets the exception the member threw, its stack kept, where a member that
/// threw before it returned reached the caller with a stack that began at the proxy.
/// </summary>
public class DispatchProxyFailedCallTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private IKitchen Tracked(Exception failure)
    {
        var services = new ServiceCollection();
        var implementation = new ClosedKitchen(failure);
        services.AddSingleton<IKitchen>(implementation);
        services.ReplaceWithTracked<IKitchen>(implementation, "Kitchen", testInfoFetcher: () => ("Kitchen Test", _testId));
        return services.BuildServiceProvider().GetRequiredService<IKitchen>();
    }

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public void A_member_that_throws_is_answered_with_the_failure()
    {
        var failure = new InvalidOperationException("The kitchen is closed.");

        var caught = Record.Exception(() => Tracked(failure).Cook());

        FailedCallContract.AssertUntouched(caught!, failure);
        FailedCallContract.AssertAnswered(Logs(), failure);
    }

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public async Task A_member_whose_task_faults_is_answered_with_the_failure()
    {
        var failure = new InvalidOperationException("The kitchen closed while it cooked.");

        var caught = await Record.ExceptionAsync(() => Tracked(failure).CookLater());

        FailedCallContract.AssertUntouched(caught!, failure);
        FailedCallContract.AssertAnswered(Logs(), failure);
    }

    public interface IKitchen
    {
        string Cook();
        Task<string> CookLater();
    }

    private sealed class ClosedKitchen(Exception failure) : IKitchen
    {
        public string Cook()
        {
            FailedCallContract.Throw(failure);
            return "";
        }

        public async Task<string> CookLater()
        {
            await Task.Yield();
            FailedCallContract.Throw(failure);
            return "";
        }
    }
}
