using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

[Collection("DiagramsFetcher")]
public class TestIdentityScopeTests
{
    [Fact]
    public void Current_is_null_when_no_scope_active()
    {
        TestIdentityScope.Reset();

        Assert.Null(TestIdentityScope.Current);
    }

    [Fact]
    public void Begin_sets_current_identity()
    {
        using (TestIdentityScope.Begin("MyTest", "test-123"))
        {
            Assert.NotNull(TestIdentityScope.Current);
            Assert.Equal("MyTest", TestIdentityScope.Current.Value.Name);
            Assert.Equal("test-123", TestIdentityScope.Current.Value.Id);
        }
    }

    [Fact]
    public void Dispose_restores_previous_null()
    {
        TestIdentityScope.Reset();

        using (TestIdentityScope.Begin("MyTest", "test-123"))
        {
            Assert.NotNull(TestIdentityScope.Current);
        }

        Assert.Null(TestIdentityScope.Current);
    }

    [Fact]
    public void Dispose_restores_previous_non_null()
    {
        using (TestIdentityScope.Begin("Outer", "outer-id"))
        {
            using (TestIdentityScope.Begin("Inner", "inner-id"))
            {
                Assert.Equal("Inner", TestIdentityScope.Current!.Value.Name);
                Assert.Equal("inner-id", TestIdentityScope.Current.Value.Id);
            }

            Assert.Equal("Outer", TestIdentityScope.Current!.Value.Name);
            Assert.Equal("outer-id", TestIdentityScope.Current.Value.Id);
        }
    }

    [Fact]
    public void Reset_clears_identity()
    {
        using (TestIdentityScope.Begin("MyTest", "test-123"))
        {
            TestIdentityScope.Reset();
            Assert.Null(TestIdentityScope.Current);
        }
    }

    [Fact]
    public async Task Scope_flows_through_await()
    {
        using (TestIdentityScope.Begin("AsyncTest", "async-id"))
        {
            await Task.Yield();
            Assert.NotNull(TestIdentityScope.Current);
            Assert.Equal("AsyncTest", TestIdentityScope.Current.Value.Name);
        }
    }

    [Fact]
    public async Task Scope_flows_into_new_thread()
    {
        using (TestIdentityScope.Begin("ThreadTest", "thread-id"))
        {
            (string Name, string Id)? capturedInThread = null;
            var thread = new Thread(() => capturedInThread = TestIdentityScope.Current);
            thread.Start();
            thread.Join();

            // AsyncLocal propagates via ExecutionContext capture on Thread.Start()
            Assert.NotNull(capturedInThread);
            Assert.Equal("ThreadTest", capturedInThread.Value.Name);
        }
    }

    [Fact]
    public async Task Scope_flows_through_Task_Run()
    {
        using (TestIdentityScope.Begin("TaskRunTest", "taskrun-id"))
        {
            var result = await Task.Run(() => TestIdentityScope.Current);

            // AsyncLocal DOES flow into Task.Run (it's on the ExecutionContext)
            Assert.NotNull(result);
            Assert.Equal("TaskRunTest", result.Value.Name);
        }
    }

    [Fact]
    public void UnknownTestName_is_Unknown()
    {
        Assert.Equal("Unknown", TestIdentityScope.UnknownTestName);
    }

    [Fact]
    public void UnknownTestId_is_unknown()
    {
        Assert.Equal("unknown", TestIdentityScope.UnknownTestId);
    }

    [Fact]
    public void UnknownIdentity_tuple_matches_individual_constants()
    {
        var (name, id) = TestIdentityScope.UnknownIdentity;
        Assert.Equal(TestIdentityScope.UnknownTestName, name);
        Assert.Equal(TestIdentityScope.UnknownTestId, id);
    }

    #region GlobalFallback

    [Fact]
    public void GlobalFallback_is_null_by_default()
    {
        TestIdentityScope.ClearGlobalFallback();

        Assert.Null(TestIdentityScope.GlobalFallback);
    }

    [Fact]
    public void SetGlobalFallback_sets_value()
    {
        try
        {
            TestIdentityScope.SetGlobalFallback("GlobalTest", "global-id");

            Assert.NotNull(TestIdentityScope.GlobalFallback);
            Assert.Equal("GlobalTest", TestIdentityScope.GlobalFallback.Value.Name);
            Assert.Equal("global-id", TestIdentityScope.GlobalFallback.Value.Id);
        }
        finally
        {
            TestIdentityScope.ClearGlobalFallback();
        }
    }

    [Fact]
    public void ClearGlobalFallback_resets_to_null()
    {
        TestIdentityScope.SetGlobalFallback("GlobalTest", "global-id");

        TestIdentityScope.ClearGlobalFallback();

        Assert.Null(TestIdentityScope.GlobalFallback);
    }

    [Fact]
    public void GlobalFallback_accessible_from_pre_existing_thread()
    {
        // This is the key use case: thread started BEFORE fallback is set
        (string Name, string Id)? capturedInThread = null;
        var readySignal = new ManualResetEventSlim(false);
        var setSignal = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            readySignal.Set();      // Signal that thread is running
            setSignal.Wait();       // Wait for fallback to be set
            capturedInThread = TestIdentityScope.GlobalFallback;
        });

        try
        {
            thread.Start();
            readySignal.Wait();     // Ensure thread is running before setting fallback

            TestIdentityScope.SetGlobalFallback("CrossThread", "cross-id");
            setSignal.Set();        // Let thread read the fallback
            thread.Join();

            Assert.NotNull(capturedInThread);
            Assert.Equal("CrossThread", capturedInThread.Value.Name);
            Assert.Equal("cross-id", capturedInThread.Value.Id);
        }
        finally
        {
            TestIdentityScope.ClearGlobalFallback();
        }
    }

    [Fact]
    public void GlobalFallback_not_affected_by_AsyncLocal_scope()
    {
        try
        {
            TestIdentityScope.SetGlobalFallback("Global", "global-id");

            using (TestIdentityScope.Begin("Local", "local-id"))
            {
                // Current should be the AsyncLocal value
                Assert.Equal("Local", TestIdentityScope.Current!.Value.Name);

                // GlobalFallback should be independent
                Assert.Equal("Global", TestIdentityScope.GlobalFallback!.Value.Name);
            }

            // After scope ends, Current is null but GlobalFallback persists
            Assert.Null(TestIdentityScope.Current);
            Assert.NotNull(TestIdentityScope.GlobalFallback);
            Assert.Equal("Global", TestIdentityScope.GlobalFallback.Value.Name);
        }
        finally
        {
            TestIdentityScope.ClearGlobalFallback();
        }
    }

    [Fact]
    public void GlobalFallback_not_visible_via_Current()
    {
        TestIdentityScope.Reset();

        try
        {
            TestIdentityScope.SetGlobalFallback("Global", "global-id");

            // Current only returns AsyncLocal, not GlobalFallback
            Assert.Null(TestIdentityScope.Current);
            Assert.NotNull(TestIdentityScope.GlobalFallback);
        }
        finally
        {
            TestIdentityScope.ClearGlobalFallback();
        }
    }

    [Fact]
    public void SetFromMessage_sets_current_identity()
    {
        TestIdentityScope.Reset();

        TestIdentityScope.SetFromMessage("MessageTest", "msg-id-1");

        Assert.NotNull(TestIdentityScope.Current);
        Assert.Equal("MessageTest", TestIdentityScope.Current.Value.Name);
        Assert.Equal("msg-id-1", TestIdentityScope.Current.Value.Id);

        TestIdentityScope.Reset();
    }

    [Fact]
    public void SetFromMessage_overwrites_previous_identity()
    {
        TestIdentityScope.Reset();

        TestIdentityScope.SetFromMessage("First", "first-id");
        TestIdentityScope.SetFromMessage("Second", "second-id");

        Assert.NotNull(TestIdentityScope.Current);
        Assert.Equal("Second", TestIdentityScope.Current.Value.Name);
        Assert.Equal("second-id", TestIdentityScope.Current.Value.Id);

        TestIdentityScope.Reset();
    }

    #endregion
}
