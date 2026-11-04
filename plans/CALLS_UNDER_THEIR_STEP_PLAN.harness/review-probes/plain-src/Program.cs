using Kronikol.Reports;
using Kronikol.Tracking;
using Probe;

var which = args.Length > 0 ? args[0] : "all";
var d = new Driver();

async Task S(string name, Func<Task> run)
{
    if (which == "all" || which == name)
    {
        Console.WriteLine();
        Console.WriteLine("#### " + name);
        await run();
    }
}

// 1. A consumer loop started inside step 0 handles work step 2 hands it.
await S("consumer", () => H.Scenario("consumer loop started in Given, handles a message from When", async id =>
{
    var inbox = System.Threading.Channels.Channel.CreateUnbounded<string>();
    await d.GivenServiceStarted(inbox, id);
    await d.WhenPublished(inbox);
    await d.ThenFinished(inbox);
}));

// 2. background task runs a woven sub-step after its step ended, with another step open.
await S("orphan", () => H.Scenario("a sub-step begun by a background task after its step ended", async id =>
{
    await d.GivenBackgroundSubStepLater();
    await d.WhenLong();
    await d.ThenDone();
    await Driver.Left;
}, settleMs: 50));

// 3. Two top-level steps at once.
await S("toplevel", () => H.Scenario("two top-level steps at once: the first takes longer", async id =>
{
    await Task.WhenAll(d.GivenSlow(), d.GivenFast());
    await d.ThenDone();
}));

// 3b. a top-level left running while the next ones run
await S("toplevel2", () => H.Scenario("a top-level step awaited later, one after it in between", async id =>
{
    var slow = d.GivenSlow();
    await d.WhenLong();
    await slow;
    await d.ThenDone();
}));

// 4. SkipIf, async, top-level, and nested.
await S("bypass-top", () => H.Scenario("async top-level step bypassed by SkipIf", async id =>
{
    await d.GivenSkipped();
    H.Call("after-bypass");
    await d.ThenDone();
}));
await S("bypass-nested", () => H.Scenario("async sub-step bypassed by SkipIf inside a parent", async id =>
{
    await d.ParentWithSkippedChild();
    await d.ThenDone();
}));
await S("bypass-nested-sync", () => H.Scenario("sync sub-step bypassed by SkipIf inside a parent", async id =>
{
    d.SyncParentWithSkippedChild();
    await d.ThenDone();
}));

// 5. Identity gone inside a step (Detach, Reset): stub starts nothing, then closes the enclosing step.
await S("detach", () => H.Scenario("a woven async step called with no identity inside a woven async step", async id =>
{
    await d.ParentCallsDetached();
    await d.ThenDone();
}));
await S("detach-gherkin", () => H.Scenario("as above under a Gherkin-style parent opened by id", async id =>
{
    StepCollector.StartStep(id, "When", "gherkin step", null, null);
    await d.CalledDetached();
    H.Call("rest-of-gherkin-step");
    Track.That(() => { });
    StepCollector.AddAttachment(id, "shot.png", "shot");
    await d.SubAfter();
    StepCollector.CompleteStep(id, false, "gherkin step failed");
}));

// 6. begin in async helper, end in the caller, after an earlier step closed in this flow.
await S("async-begin", () => H.Scenario("StartStep in an async helper, CompleteStep in the caller", async id =>
{
    StepCollector.StartStep(id, "Given", "first", null, null);
    H.Call("first");
    StepCollector.CompleteStep(id, true);
    await Driver.BeginAsync(id, "When", "second");
    H.Call("second");
    Track.That(() => { });
    StepCollector.AddAttachment(id, "shot.png", "shot");
    StepCollector.CompleteStep(id, false, "second failed");
    StepCollector.StartStep(id, "Then", "third", null, null);
    H.Call("third");
    StepCollector.CompleteStep(id, true);
}));

// 7. Service thread, parent's own call, one sub-step running.
await S("service-thread", () => H.Scenario("the parent's own service-side call while one sub-step runs", async id =>
{
    await d.ParentCallsWhileChildRuns(id);
    await d.ThenDone();
}));

// 8. A thread with no flow closes "the current step" while an async step runs.
await S("foreign-close", () => H.Scenario("CompleteStep from a thread with no flow while an async step runs", async id =>
{
    var a = d.GivenSlow();
    var done = new TaskCompletionSource();
    ThreadPool.UnsafeQueueUserWorkItem(_ => { StepCollector.CompleteStep(id, true); done.SetResult(); }, null);
    await done.Task;
    await a;
    await d.ThenDone();
}));

// 9. Assertion and attachment from a flow whose step has ended, with a later step open.
await S("stale-assert", () => H.Scenario("an assertion from a flow whose step ended, a later step open", async id =>
{
    await d.GivenStaleAssertionLater();
    await d.WhenLong();
    await Driver.Left;
    await d.ThenDone();
}));

// 10. A sub-step that throws before it returns a task.
await S("throws-sync", () => H.Scenario("a Task-returning sub-step that throws before returning a task", async id =>
{
    try { await d.ParentCallsThrower(); } catch (InvalidOperationException) { }
    await d.ThenDone();
}));

// 11. A hung sub-step, the parent gives up on it.
await S("hung", () => H.Scenario("a sub-step still running when its parent times out", async id =>
{
    try { await d.ParentTimesOut(); } catch (TimeoutException) { }
    await d.ThenDone();
}));

// 12. Top-level bypass loses the flow's Root, so a sub-step left running swallows the next step again.
await S("bypass-root", () => H.Scenario("a sub-step left running, an async bypass, then the next step", async id =>
{
    await d.GivenLeavesChildRunning();
    await d.WhenPlain();
    await d.GivenSkipped();
    await d.ThenDone();
    await Driver.Left;
}));

// 13. No-identity stub under a parent opened by id: the parent closes early.
await S("detach-parent", () => H.Scenario("no-identity stub directly under a step opened by id", async id =>
{
    StepCollector.StartStep(id, "When", "gherkin step", null, null);
    using (TestIdentityScope.Detach())
        await d.Inner();
    H.Call("rest-of-gherkin-step");
    Track.That(() => { });
    StepCollector.AddAttachment(id, "shot.png", "shot");
    await d.SubAfter();
    StepCollector.CompleteStep(id, false, "gherkin step failed");
}));

// 14. StartStep on a child flow, CompleteStep on the parent flow, a chain open.
await S("child-start", () => H.Scenario("StartStep on a child flow, then two CompleteStep on the parent flow", async id =>
{
    StepCollector.StartStep(id, "Given", "A", null, null);
    await Task.Run(() => StepCollector.StartStep(id, null, "B started on a child flow", null, null));
    H.Call("while-b-open");
    StepCollector.CompleteStep(id, false, "b failed");
    StepCollector.CompleteStep(id, true);
    StepCollector.StartStep(id, "Then", "C", null, null);
    StepCollector.CompleteStep(id, true);
}));

// 15. The real thing: a generic host built inside the Given, its hosted service detached by Kronikol's own wrapper,
// a consumer naming the scenario per message with SetFromMessage.
await S("host", () => H.Scenario("a host built inside the Given, its hosted consumer named per message", async id =>
{
    var inbox = System.Threading.Channels.Channel.CreateUnbounded<string>();
    var host = await d.GivenHostIsBuilt(inbox, id);
    await d.WhenPublished(inbox);
    await d.ThenFinished(inbox);
    inbox.Writer.Complete();
    await host.StopAsync();
}));

// 16. A stored assertion task, the shape Track.AssertionTask documents: made in one step, ending in a later one.
await S("stored-assertion", () => H.Scenario("an assertion task stored in the Given, ending during the When", async id =>
{
    await d.GivenStoresAnAssertion();
    await d.WhenLong();
    try { await Driver.Left; } catch (InvalidOperationException) { }
    await d.ThenDone();
}));

public sealed class Consumer(System.Threading.Channels.Channel<string> inbox, string id) : Microsoft.Extensions.Hosting.BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in inbox.Reader.ReadAllAsync(stoppingToken))
        {
            TestIdentityScope.SetFromMessage(id, id);
            H.Call("consumer/" + message, id);
            TestIdentityScope.ClearMessageIdentity();
        }
    }
}

public class Driver
{
    [GivenStep(Description = "an assertion task is stored")]
    public Task GivenStoresAnAssertion()
    {
        Left = Track.AssertionTask(FailsLater(), "the slow call should succeed");
        return Task.CompletedTask;
    }

    private static async Task FailsLater()
    {
        await Task.Delay(60);
        throw new InvalidOperationException("the slow call failed");
    }

    [GivenStep(Description = "the host is built")]
    public async Task<Microsoft.Extensions.Hosting.IHost> GivenHostIsBuilt(System.Threading.Channels.Channel<string> inbox, string id)
    {
        H.Call("given-own");
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionHostedServiceExtensions.AddHostedService(builder.Services, _ => new Consumer(inbox, id));
        builder.Services.DetachHostedServicesFromTestIdentity();
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    [GivenStep(Description = "a background assertion will run later")]
    public Task GivenStaleAssertionLater()
    {
        Left = Task.Run(async () =>
        {
            await Task.Delay(60);
            try { Track.That(() => throw new InvalidOperationException("background check failed")); } catch { }
            StepCollector.AddAttachment(H.Id, "bg.png", "bg");
        });
        return Task.CompletedTask;
    }

    [Step(Description = "throws before returning a task")]
    public Task ThrowsSync() => throw new InvalidOperationException("thrown before a task");

    [GivenStep(Description = "parent of a thrower")]
    public async Task ParentCallsThrower()
    {
        await Task.Yield();
        await ThrowsSync();
    }

    [Step(Description = "hangs")]
    public async Task Hangs() => await Task.Delay(1000);

    [GivenStep(Description = "parent that times out")]
    public async Task ParentTimesOut()
    {
        var hung = Hangs();
        if (await Task.WhenAny(hung, Task.Delay(30)) != hung)
            throw new TimeoutException("gave up waiting for the sub-step");
    }

    [GivenStep(Description = "leaves a child running")]
    public Task GivenLeavesChildRunning()
    {
        Left = Hangs200();
        return Task.CompletedTask;
    }

    [Step(Description = "child left running")]
    public async Task Hangs200() => await Task.Delay(200);

    [WhenStep(Description = "plain when")]
    public Task WhenPlain() { H.Call("plain-when"); return Task.CompletedTask; }

    public static Task Left;
    public bool Skip => true;

    [GivenStep(Description = "the service is started")]
    public async Task GivenServiceStarted(System.Threading.Channels.Channel<string> inbox, string id)
    {
        H.Call("given-own");
        // what a host does: its hosted service starts detached from identity, and names the scenario per message
        using (TestIdentityScope.Detach())
        {
            _ = Task.Run(async () =>
            {
                await foreach (var message in inbox.Reader.ReadAllAsync())
                {
                    using (TestIdentityScope.Begin(id, id))
                        H.Call("consumer/" + message);
                }
            });
        }
        await Task.Yield();
    }

    [WhenStep(Description = "a message is published")]
    public async Task WhenPublished(System.Threading.Channels.Channel<string> inbox)
    {
        H.Call("when-own");
        await inbox.Writer.WriteAsync("from-when");
        await Task.Delay(100);
    }

    [ThenStep(Description = "it is finished")]
    public async Task ThenFinished(System.Threading.Channels.Channel<string> inbox)
    {
        H.Call("then-own");
        await inbox.Writer.WriteAsync("from-then");
        await Task.Delay(100);
    }

    [GivenStep(Description = "a background task will run a sub-step")]
    public Task GivenBackgroundSubStepLater()
    {
        H.Call("given-own");
        Left = Task.Run(async () =>
        {
            await Task.Delay(60);
            await SubLater();
        });
        return Task.CompletedTask;
    }

    [Step(Description = "the later sub-step")]
    public async Task SubLater()
    {
        H.Call("sub-later");
        await Task.Delay(10);
    }

    [WhenStep(Description = "a long step")]
    public async Task WhenLong()
    {
        H.Call("when-own/1");
        await Task.Delay(150);
        H.Call("when-own/2");
    }

    [ThenStep(Description = "done")]
    public async Task ThenDone()
    {
        H.Call("then-own");
        await Task.Yield();
    }

    [GivenStep(Description = "slow")]
    public async Task GivenSlow()
    {
        H.Call("slow/1");
        await Task.Delay(120);
        H.Call("slow/2");
    }

    [GivenStep(Description = "fast")]
    public async Task GivenFast()
    {
        H.Call("fast/1");
        await Task.Delay(20);
        H.Call("fast/2");
    }

    [GivenStep(Description = "skipped", SkipIf = nameof(Skip), SkipReason = "because")]
    public async Task GivenSkipped()
    {
        H.Call("never");
        await Task.Yield();
    }

    [Step(Description = "skipped child", SkipIf = nameof(Skip), SkipReason = "because")]
    public async Task SkippedChild()
    {
        H.Call("never");
        await Task.Yield();
    }

    [Step(Description = "skipped sync child", SkipIf = nameof(Skip), SkipReason = "because")]
    public void SkippedSyncChild()
    {
        H.Call("never");
    }

    [Step(Description = "sibling after")]
    public async Task SiblingAfter()
    {
        H.Call("sibling-after");
        await Task.Yield();
    }

    [GivenStep(Description = "parent of a skipped child")]
    public async Task ParentWithSkippedChild()
    {
        H.Call("parent/before");
        await SkippedChild();
        H.Call("parent/after");
        Track.That(() => { });
        await SiblingAfter();
        await Task.Delay(5);
        throw new InvalidOperationException("parent failed after the skipped child");
    }

    [GivenStep(Description = "sync parent of a skipped child")]
    public void SyncParentWithSkippedChild()
    {
        H.Call("parent/before");
        SkippedSyncChild();
        H.Call("parent/after");
        Track.That(() => { });
    }

    [Step(Description = "a step called with no identity")]
    public async Task CalledDetached()
    {
        using (TestIdentityScope.Detach())
            await Inner();
    }

    [Step(Description = "inner, identity gone")]
    public async Task Inner()
    {
        await Task.Delay(10);
    }

    [Step(Description = "sub after")]
    public async Task SubAfter()
    {
        H.Call("sub-after");
        await Task.Yield();
    }

    [GivenStep(Description = "parent that detaches")]
    public async Task ParentCallsDetached()
    {
        H.Call("parent/before");
        using (TestIdentityScope.Detach())
            await Inner();
        H.Call("parent/after");
        await Task.Delay(10);
    }

    public static async Task BeginAsync(string id, string keyword, string text)
    {
        StepCollector.StartStep(id, keyword, text, null, null);
        await Task.Yield();
    }

    [GivenStep(Description = "parent")]
    public async Task ParentCallsWhileChildRuns(string id)
    {
        var child = ChildRunning();
        H.Call("parent-client-side");
        // the parent's request reaches the service, which logs its own dependency call on a thread with no flow
        var done = new TaskCompletionSource();
        ThreadPool.UnsafeQueueUserWorkItem(_ => { H.Call("service-side-of-parent-call", id); done.SetResult(); }, null);
        await done.Task;
        await child;
    }

    [Step(Description = "child running")]
    public async Task ChildRunning()
    {
        H.Call("child-own");
        await Task.Delay(80);
    }
}
