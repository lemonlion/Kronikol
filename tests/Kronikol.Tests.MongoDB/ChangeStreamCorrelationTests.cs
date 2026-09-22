using Kronikol.Extensions.MongoDB;
using Kronikol.Tracking;

namespace Kronikol.Tests.MongoDB;

// Clears the process-wide correlation store and identity scope in its constructor and Dispose: in the same
// collection as MongoDbTrackingSubscriberTests, which relies on both, or xUnit runs the two in parallel and
// a Clear here lands between that class's insert and its claim (seen on CI: the owner and flow attributions
// gone, [TestContext] alone).
[Collection("TestCorrelationStore")]
public class ChangeStreamCorrelationTests : IDisposable
{
    public ChangeStreamCorrelationTests()
    {
        TestCorrelationStore.Clear();
        TestIdentityScope.Reset();
    }

    public void Dispose()
    {
        TestCorrelationStore.Clear();
        TestIdentityScope.Reset();
    }

    private record UserDocument(string Id, string Name);

    [Fact]
    public async Task Wrap_Sets_TestIdentityScope_From_CorrelationStore()
    {
        TestCorrelationStore.Correlate(CorrelationKeys.Mongo("Users", "user-1"), "Test A", "id-a");

        (string Name, string Id)? capturedIdentity = null;
        var wrapped = ChangeStreamCorrelation.Wrap<UserDocument>(
            (item, ct) =>
            {
                capturedIdentity = TestIdentityScope.Current;
                return Task.CompletedTask;
            }, "Users", doc => doc.Id);

        await wrapped(new UserDocument("user-1", "Alice"), CancellationToken.None);

        Assert.NotNull(capturedIdentity);
        Assert.Equal("Test A", capturedIdentity.Value.Name);
        Assert.Equal("id-a", capturedIdentity.Value.Id);
    }

    [Fact]
    public async Task Wrap_NoOp_When_Key_Not_Found()
    {
        (string Name, string Id)? capturedIdentity = null;
        var wrapped = ChangeStreamCorrelation.Wrap<UserDocument>(
            (item, ct) =>
            {
                capturedIdentity = TestIdentityScope.Current;
                return Task.CompletedTask;
            }, "Users", doc => doc.Id);

        await wrapped(new UserDocument("unknown", "Bob"), CancellationToken.None);

        Assert.Null(capturedIdentity);
    }

    [Fact]
    public async Task WrapBatch_Uses_First_Match()
    {
        TestCorrelationStore.Correlate(CorrelationKeys.Mongo("Users", "user-2"), "Test B", "id-b");

        (string Name, string Id)? capturedIdentity = null;
        var wrapped = ChangeStreamCorrelation.WrapBatch<UserDocument>(
            (items, ct) =>
            {
                capturedIdentity = TestIdentityScope.Current;
                return Task.CompletedTask;
            }, "Users", doc => doc.Id);

        await wrapped([new("unknown", "X"), new("user-2", "Y")], CancellationToken.None);

        Assert.NotNull(capturedIdentity);
        Assert.Equal("Test B", capturedIdentity.Value.Name);
    }

    [Fact]
    public async Task Wrap_Scope_Disposed_On_Exception()
    {
        TestCorrelationStore.Correlate(CorrelationKeys.Mongo("Users", "user-1"), "Test A", "id-a");

        var wrapped = ChangeStreamCorrelation.Wrap<UserDocument>(
            (_, _) => throw new InvalidOperationException("boom"),
            "Users", doc => doc.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => wrapped(new UserDocument("user-1", "Alice"), CancellationToken.None));

        Assert.Null(TestIdentityScope.Current);
    }
}
