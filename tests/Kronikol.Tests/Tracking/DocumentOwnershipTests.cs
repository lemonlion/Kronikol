using System.Net;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// The owner window (plans/OWNER_WINDOW_AND_COUNT_CONFIRMATION_PLAN.md): a detached flow that wrote a
/// scenario's document is doing that scenario's work until its next operation on a document that is not
/// the scenario's. What the flow does in between is held back and attributed when the next operation on the
/// document confirms it, or dropped when a foreign operation or a query comes first, so two workers that
/// interleave in one flow can never hand one scenario's call to another.
/// </summary>
[Collection("DiagramsFetcher")]
public class DocumentOwnershipTests
{
    private readonly string _run = Guid.NewGuid().ToString("N");

    private string Key(string document) => $"cosmos:orders:{_run}:{document}";

    /// <summary>A scenario that wrote its document under its own context, as the store records it: Pay wrote x, Refund wrote y.</summary>
    private TestIdentity Owner(string name = "Pay")
    {
        var owner = new TestIdentity(name, _run + ":" + name, AttributionSource.TestContext);
        TestCorrelationStore.Correlate(Key(name == "Pay" ? "x" : "y"), owner.Name, owner.Id);
        return owner;
    }

    private static RequestResponseLog[] Logged(string testId) =>
        RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();

    private static RequestResponseLog[] LoggedAt(string uri) =>
        RequestResponseLogger.RequestAndResponseLogs.Where(l => l.Uri.ToString() == uri).ToArray();

    /// <summary>A tracked call that names no document, resolved as every tracker resolves: the dispatch.</summary>
    private string Call(string what)
    {
        var uri = $"http://dispatcher/{_run}/{what}";
        RequestResponseLogger.LogPair(HttpMethod.Post, new Uri(uri), "Dispatcher", "SUT");
        return uri;
    }

    /// <summary>What a document tracker does for one operation: resolve, ask ownership, and after a write, say so.</summary>
    private static TestIdentity? Operation(string? key, DocumentOperationKind kind)
    {
        var resolved = TestInfoResolver.ResolveWithSource(null, (Func<(string, string)>?)null);
        var identity = DocumentOwnership.ForOperation(resolved, key, kind);
        if (identity is { } who && kind == DocumentOperationKind.Write && key is not null)
            DocumentOwnership.AfterWrite(who);
        return identity;
    }

    /// <summary>Runs the tracker's part inside an awaited async callee, as it runs inside a database SDK.</summary>
    private static async Task<TestIdentity?> Deep(string? key, DocumentOperationKind kind)
    {
        await Task.Yield();
        return Operation(key, kind);
    }

    [Fact]
    public async Task A_write_on_a_scenarios_document_opens_the_window_and_the_next_operation_on_it_confirms_what_came_between()
    {
        var pay = Owner();
        using (TestIdentityScope.Detach())
        {
            var claim = await Deep(Key("x"), DocumentOperationKind.Write);

            Assert.Equal((pay.Id, AttributionSource.DocumentOwner), (claim!.Value.Id, claim.Value.Source));
            Assert.Equal((pay.Name, pay.Id), TestIdentityScope.OwnerWindow);

            var dispatch = Call("dispatch");
            Assert.Empty(Logged(pay.Id)); // held back until the flow returns to the document

            await Deep(Key("x"), DocumentOperationKind.Write);

            var logged = Logged(pay.Id);
            Assert.Equal(2, logged.Length);
            Assert.All(logged, l => Assert.Equal((dispatch, AttributionSource.DocumentFlow), (l.Uri.ToString(), l.AttributionSource)));
            Assert.Equal((pay.Name, pay.Id), TestIdentityScope.OwnerWindow);
        }
        Assert.Null(TestIdentityScope.OwnerWindow);
    }

    [Fact]
    public void A_query_ends_the_window_and_drops_what_it_held()
    {
        // The poll names no document: whatever the flow did since the write was not closed out by the
        // document, and is nobody's. After the poll the flow resolves to nobody again.
        var pay = Owner();
        using (TestIdentityScope.Detach())
        {
            Operation(Key("x"), DocumentOperationKind.Write);
            Call("dispatch");

            Assert.Null(Operation(null, DocumentOperationKind.Query));
            Assert.Null(TestIdentityScope.OwnerWindow);

            Call("orphan");
        }

        Assert.Empty(Logged(pay.Id));
    }

    [Fact]
    public void Another_scenarios_document_ends_the_window_and_a_write_on_it_opens_its_own()
    {
        var pay = Owner("Pay");
        var refund = Owner("Refund");
        using (TestIdentityScope.Detach())
        {
            Operation(Key("x"), DocumentOperationKind.Write);
            Call("between");

            var theirs = Operation(Key("y"), DocumentOperationKind.Write);
            Assert.Equal((refund.Id, AttributionSource.DocumentOwner), (theirs!.Value.Id, theirs.Value.Source));
            Assert.Equal((refund.Name, refund.Id), TestIdentityScope.OwnerWindow);

            var dispatch = Call("theirs");
            Operation(Key("y"), DocumentOperationKind.Read);

            Assert.Empty(Logged(pay.Id));
            Assert.Equal(2, Logged(refund.Id).Length);
            Assert.All(Logged(refund.Id), l => Assert.Equal((dispatch, AttributionSource.DocumentFlow), (l.Uri.ToString(), l.AttributionSource)));
        }
    }

    [Fact]
    public void A_read_confirms_a_window_but_never_opens_one()
    {
        // A poller reading the scenario's document every few hundred milliseconds must not make everything
        // between its reads the scenario's.
        var pay = Owner();
        using (TestIdentityScope.Detach())
        {
            var read = Operation(Key("x"), DocumentOperationKind.Read);
            Assert.Equal((pay.Id, AttributionSource.DocumentOwner), (read!.Value.Id, read.Value.Source));
            Assert.Null(TestIdentityScope.OwnerWindow);

            var uri = Call("after-read");
            Operation(Key("x"), DocumentOperationKind.Read);

            Assert.Empty(LoggedAt(uri));
        }
    }

    [Fact]
    public void A_document_nobody_owns_and_a_document_not_yet_known_leave_the_window_as_it_is()
    {
        // An audit row the processor creates, a config document it reads: neither is another scenario's,
        // and neither closes out the owner's work. They are not the owner's either.
        var pay = Owner();
        using (TestIdentityScope.Detach())
        {
            Operation(Key("x"), DocumentOperationKind.Write);
            var dispatch = Call("dispatch");

            Assert.Null(Operation(Key("nobody-wrote-this"), DocumentOperationKind.Write));
            Assert.Null(Operation(Key("nobody-wrote-this"), DocumentOperationKind.Read));
            Assert.Null(Operation(null, DocumentOperationKind.Write)); // a create: the id is in a body the tracker has not parsed
            Assert.Equal((pay.Name, pay.Id), TestIdentityScope.OwnerWindow);

            Operation(Key("x"), DocumentOperationKind.Read);

            Assert.Equal(2, LoggedAt(dispatch).Length);
        }
    }

    [Fact]
    public void A_call_with_a_scenario_of_its_own_is_never_re_attributed_and_leaves_the_window_alone()
    {
        var pay = Owner("Pay");
        Owner("Refund");
        using (TestIdentityScope.Detach())
        {
            Operation(Key("x"), DocumentOperationKind.Write);
            using (TestIdentityScope.Begin("Correlated", "c-" + _run))
            {
                var who = Operation(Key("y"), DocumentOperationKind.Write);
                Assert.Equal(("c-" + _run, AttributionSource.Scope), (who!.Value.Id, who.Value.Source));
            }
            Assert.Equal((pay.Name, pay.Id), TestIdentityScope.OwnerWindow);
        }
    }

    [Fact]
    public void Background_capture_keeps_what_a_closed_window_dropped_under_no_scenario()
    {
        Owner();
        string dispatch;
        RequestResponseLogger.CaptureBackground = true;
        try
        {
            using (TestIdentityScope.Detach())
            {
                Operation(Key("x"), DocumentOperationKind.Write);
                dispatch = Call("dropped");
                Operation(null, DocumentOperationKind.Query);
            }
        }
        finally
        {
            RequestResponseLogger.CaptureBackground = false;
        }

        var background = LoggedAt(dispatch);
        Assert.Equal(2, background.Length);
        Assert.All(background, l => Assert.Equal((TestIdentityScope.UnknownTestId, AttributionSource.Detached), (l.TestId, l.AttributionSource)));
    }

    [Fact]
    public void An_unknown_owner_in_the_store_is_no_owner()
    {
        // Nothing registers the unknown identity any more; a store a consumer filled by hand may still hold it.
        TestCorrelationStore.Correlate(Key("x"), TestIdentityScope.UnknownTestName, TestIdentityScope.UnknownTestId);
        using (TestIdentityScope.Detach())
        {
            Assert.Null(DocumentOwnership.Resolve(null, Key("x")));
            Assert.Null(Operation(Key("x"), DocumentOperationKind.Write));
            Assert.Null(TestIdentityScope.OwnerWindow);
        }
    }

    [Fact]
    public void What_no_operation_confirmed_by_report_time_is_settled_as_nobodys()
    {
        // The processor claimed the row and the run ended before it came back to it: the report cannot wait.
        var pay = Owner();
        string dispatch;
        RequestResponseLogger.CaptureBackground = true;
        try
        {
            using (TestIdentityScope.Detach())
            {
                Operation(Key("x"), DocumentOperationKind.Write);
                dispatch = Call("unsettled");
                DocumentOwnership.Settle();
                Operation(Key("x"), DocumentOperationKind.Read); // too late to confirm what was settled
            }
        }
        finally
        {
            RequestResponseLogger.CaptureBackground = false;
        }

        var settled = LoggedAt(dispatch);
        Assert.Equal(2, settled.Length);
        Assert.All(settled, l => Assert.Equal((TestIdentityScope.UnknownTestId, AttributionSource.Detached), (l.TestId, l.AttributionSource)));
        Assert.Empty(Logged(pay.Id));
    }

    [Fact]
    public void Outside_a_detached_flow_ownership_is_per_call_as_before()
    {
        var pay = Owner();

        var claim = Operation(Key("x"), DocumentOperationKind.Write);
        Assert.Equal((pay.Id, AttributionSource.DocumentOwner), (claim!.Value.Id, claim.Value.Source));
        Assert.Null(TestIdentityScope.OwnerWindow);

        var uri = Call("nowhere");
        Operation(Key("x"), DocumentOperationKind.Read);

        Assert.Empty(LoggedAt(uri));
    }

    [Fact]
    public async Task Work_a_detached_flow_started_shares_its_window()
    {
        // A hosted service's loop is started inside the detachment and runs on after it: the loop and every
        // callee of the loop read and write the same window.
        var pay = Owner();
        var dispatch = "";
        Task loop;
        using (TestIdentityScope.Detach())
            loop = Task.Run(async () =>
            {
                await Deep(Key("x"), DocumentOperationKind.Write);
                dispatch = Call("loop");
                await Deep(Key("x"), DocumentOperationKind.Read);
            });
        await loop;

        Assert.Equal(2, LoggedAt(dispatch).Length);
        Assert.All(LoggedAt(dispatch), l => Assert.Equal((pay.Id, AttributionSource.DocumentFlow), (l.TestId, l.AttributionSource)));
    }
}
