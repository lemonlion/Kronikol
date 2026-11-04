    // ---- a step on a flow with no chain ----

    /// <summary>Runs work on a thread of its own under a TestIdentityScope set by hand. UnsafeStart flows no execution context, so the thread has no step chain and no ambient phase; Start flows both.</summary>
    private void OnThread(bool flowContext, bool withScope, Action work)
    {
        var (name, id) = ProbeEnv.Fetcher();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (withScope)
                    using (TestIdentityScope.Begin(name, id))
                        work();
                else
                    work();
            }
            catch (Exception e) { failure = e; }
        });
        if (flowContext) thread.Start(); else thread.UnsafeStart();
        thread.Join();
        if (failure is not null) throw failure;
    }

    [GivenStep]
    public async Task GivenWithWhenOnThread(string label, bool flowContext)
    {
        await _calls.Make($"{label}-before-thread");
        OnThread(flowContext, withScope: true, () =>
        {
            WhenSync($"{label}-when-on-thread");
            _calls.Make($"{label}-after-when-on-thread").GetAwaiter().GetResult();
        });
        await _calls.Make($"{label}-after-thread");
    }

    [GivenStep]
    public async Task GivenWithAsyncWhenOnThread(string label)
    {
        await _calls.Make($"{label}-before-thread");
        OnThread(flowContext: false, withScope: true, () => WhenAsync($"{label}-when-on-thread").GetAwaiter().GetResult());
        await _calls.Make($"{label}-after-thread");
    }

    [WhenStep]
    public async Task WhenWithGivenOnThread(string label)
    {
        await _calls.Make($"{label}-before-thread");
        OnThread(flowContext: false, withScope: true, () =>
        {
            GivenSync($"{label}-given-on-thread");
            _calls.Make($"{label}-after-given-on-thread").GetAwaiter().GetResult();
        });
        await _calls.Make($"{label}-after-thread");
    }

    [Fact]
    public async Task T01_a_When_on_an_unflowed_thread_inside_a_Given()
    {
        await GivenWithWhenOnThread("t01", flowContext: false);
        await _calls.Make("t01-after-given");
        await WhenAsync("t01-when");
        await _calls.Make("t01-after-when");
    }

    [Fact]
    public async Task T02_a_When_on_a_flowed_thread_inside_a_Given()
    {
        await GivenWithWhenOnThread("t02", flowContext: true);
        await _calls.Make("t02-after-given");
        await WhenAsync("t02-when");
        await _calls.Make("t02-after-when");
    }

    [Fact]
    public async Task T03_a_When_on_an_unflowed_thread_while_a_Given_is_left_running()
    {
        var gate = new TaskCompletionSource();
        var open = GivenOpen("t03-open-given", gate);
        OnThread(flowContext: false, withScope: true, () =>
        {
            WhenSync("t03-when-on-thread");
            _calls.Make("t03-after-when-on-thread").GetAwaiter().GetResult();
        });
        await _calls.Make("t03-while-open");
        gate.SetResult();
        await open;
        await _calls.Make("t03-after-closed");
        await WhenAsync("t03-when");
        await _calls.Make("t03-after-when");
    }

    [Fact]
    public async Task T04_an_async_When_on_an_unflowed_thread_inside_a_Given()
    {
        await GivenWithAsyncWhenOnThread("t04");
        await _calls.Make("t04-after-given");
        await WhenAsync("t04-when");
        await _calls.Make("t04-after-when");
    }

    [Fact]
    public async Task T05_a_Given_on_an_unflowed_thread_inside_a_When_after_a_Given()
    {
        await GivenAsync("t05-given");
        await WhenWithGivenOnThread("t05");
        await _calls.Make("t05-after-when");
        await ThenAsync("t05-then");
    }

    [Fact]
    public async Task T06_a_Given_on_an_unflowed_thread_inside_a_first_When_then_a_Then()
    {
        await WhenWithGivenOnThread("t06");
        await _calls.Make("t06-after-when");
        await ThenAsync("t06-then");
        await _calls.Make("t06-after-then");
    }

    [Fact]
    public async Task T07_a_When_on_an_unflowed_thread_with_no_identity_inside_a_Given()
    {
        await GivenWithNoIdentityWhenOnThread("t07");
        await _calls.Make("t07-after-given");
    }

    [GivenStep]
    public async Task GivenWithNoIdentityWhenOnThread(string label)
    {
        await _calls.Make($"{label}-before-thread");
        OnThread(flowContext: false, withScope: false, () => WhenSync($"{label}-when-on-thread"));
        await _calls.Make($"{label}-after-thread");
    }

    // ---- Task steps that are not async, in shapes other than X16's and X25's: the patch gives each stub a catch ----

    public bool Never => false;

    private async Task<int> MakeInt(string label)
    {
        await _calls.Make(label);
        return 1;
    }

    private async Task<T> MakeGeneric<T>(string label, T value)
    {
        await _calls.Make(label);
        return value;
    }

    [GivenStep] public Task<int> GivenTaskOfTNoAsync(string label) => MakeInt(label);

    [GivenStep]
    public Task GivenTaskWithBranches(string label, bool first)
    {
        if (first)
            return _calls.Make($"{label}-first");
        return _calls.Make($"{label}-second");
    }

    [GivenStep(SkipIf = nameof(Always), SkipReason = "skipped")] public Task GivenTaskSkipped(string label) => _calls.Make(label);
    [GivenStep(SkipIf = nameof(Always), SkipReason = "skipped")] public Task<int> GivenTaskOfTSkipped(string label) => MakeInt(label);
    [WhenStep(SkipIf = nameof(Never), SkipReason = "skipped")] public Task WhenTaskNotSkipped(string label) => _calls.Make(label);
    [GivenStep] public Task<T> GivenTaskGeneric<T>(string label, T value) => MakeGeneric(label, value);

    [WhenStep]
    public Task WhenTaskInTryFinally(string label)
    {
        try
        {
            return _calls.Make(label);
        }
        finally
        {
            _ = label.Length;
        }
    }

    [WhenStep]
    public Task WhenTaskWithUsing(string label)
    {
        using var gate = new SemaphoreSlim(1);
        return _calls.Make(label);
    }

    [GivenStep] public Task<int> GivenTaskOfTThrows(string label) => throw new InvalidOperationException(label);
    [GivenStep] public Task GivenTaskReturnsFaulted(string label) => Task.FromException(new InvalidOperationException(label));

    [WhenStep]
    public Task WhenTaskCatchesInside(string label)
    {
        try
        {
            throw new InvalidOperationException(label);
        }
        catch (InvalidOperationException)
        {
            return _calls.Make(label);
        }
    }

    [Fact]
    public async Task T10_Task_steps_that_are_not_async_in_their_other_shapes()
    {
        await GivenTaskOfTNoAsync("t10-of-t");
        await GivenTaskWithBranches("t10-branch", first: false);
        await GivenTaskSkipped("t10-skipped");
        await GivenTaskOfTSkipped("t10-of-t-skipped");
        await _calls.Make("t10-after-skipped");
        await GivenTaskGeneric("t10-generic", 1);
        await WhenTaskNotSkipped("t10-not-skipped");
        await WhenTaskInTryFinally("t10-try-finally");
        await WhenTaskWithUsing("t10-using");
        await WhenTaskCatchesInside("t10-catches-inside");
        await _calls.Make("t10-after");
    }

    [Fact]
    public async Task T11_a_Task_of_T_step_that_throws_before_returning_then_a_When()
    {
        try { await GivenTaskOfTThrows("t11-throws"); }
        catch (InvalidOperationException) { }
        await _calls.Make("t11-after-throw");
        await WhenAsync("t11-when");
    }

    [Fact]
    public async Task T12_a_Task_step_returning_a_faulted_task_then_a_When()
    {
        try { await GivenTaskReturnsFaulted("t12-faulted"); }
        catch (InvalidOperationException) { }
        await _calls.Make("t12-after-fault");
        await WhenAsync("t12-when");
    }

    [Fact]
    public void T13_a_Task_step_that_throws_before_returning_never_awaited()
    {
        Assert_throws(() => GivenThrowsBeforeReturning("t13-throws"));
        WhenSync("t13-when");
    }

    private static void Assert_throws(Func<Task> action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException("the step did not throw");
    }

    [GivenStep]
    public async Task GivenWithNestedThrowingTask(string label)
    {
        try { await GivenThrowsBeforeReturning($"{label}-nested-throws"); }
        catch (InvalidOperationException) { }
        await _calls.Make($"{label}-after-nested-throw");
        await WhenAsync($"{label}-nested-when");
    }

    [Fact]
    public async Task T14_a_nested_Task_step_that_throws_before_returning_in_a_Given()
    {
        await GivenWithNestedThrowingTask("t14");
        await WhenAsync("t14-when");
    }
