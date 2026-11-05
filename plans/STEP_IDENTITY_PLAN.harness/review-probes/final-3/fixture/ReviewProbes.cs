using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Tracking;
using TestStack.BDDfy;
using TestStack.BDDfy.Configuration;
using Xunit;

namespace EngineFixtures;

// TEMPORARY: the review's shapes. Removed when the review ends.

/// <summary>A consumer's own executor set around Kronikol's, by the step's title.</summary>
public sealed class ReviewOuterExecutor(IStepExecutor inner) : IStepExecutor
{
    public object Execute(Step step, object testObject)
    {
        if (step.Title.Contains("dropped", StringComparison.OrdinalIgnoreCase))
        {
            inner.Execute(step, testObject);
            return null!;
        }

        if (step.Title.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            var result = inner.Execute(step, testObject);
            if (result is Task task && !task.Wait(TimeSpan.FromMilliseconds(150)))
                throw new TimeoutException("the step took too long");
            return result;
        }

        if (step.Title.Contains("waits itself", StringComparison.OrdinalIgnoreCase))
        {
            var result = inner.Execute(step, testObject);
            if (result is Task task)
                task.GetAwaiter().GetResult();
            return null!;
        }

        return inner.Execute(step, testObject);
    }
}

public static class ReviewSetup
{
    public static void Install() => Configurator.StepExecutor = new ReviewOuterExecutor(Configurator.StepExecutor);

    public static void Dump()
    {
        var path = Environment.GetEnvironmentVariable("REVIEW_DUMP");
        if (path is null)
            return;
        var lines = BDDfyScenarioCollector.GetAll()
            .Where(s => s.ScenarioTitle.StartsWith("RV", StringComparison.Ordinal) || s.ScenarioTitle.StartsWith("AN4", StringComparison.Ordinal))
            .OrderBy(s => s.ScenarioTitle, StringComparer.Ordinal)
            .Select(s => $"== {s.ScenarioTitle} result={s.Result}\nmessage: {s.ErrorMessage}\nstack:\n{s.ErrorStackTrace}\n");
        File.WriteAllText(path, string.Join("\n", lines));
    }
}

public class ReviewFiles
{
    private readonly Driver _driver = new();
    private void GivenARegisteredCustomer() => _driver.A_customer_is_registered();
    private void ThenTheOrderIsConfirmed() => _driver.The_order_is_read_back();
    private void CleanupWithAFile() => Files.Attach("rv1-teardown-file");
    private void GivenAFileNamedAfter(string customer) => Files.Attach("rv3-row-" + customer);

    // Fix 5: a rebuilt row (Probe.cs rebuilds Steps[1] of RV1 and RV3) and a tear down holding a file.
    [Fact]
    public void RV1_a_rebuilt_row_and_a_file_in_a_tear_down() =>
        this.Given(x => x.GivenARegisteredCustomer()).Then(x => x.ThenTheOrderIsConfirmed()).TearDownWith(x => x.CleanupWithAFile()).BDDfy();

    // Fix 5: a rebuilt row and an example table's later row holding a file.
    [Fact]
    public void RV3_a_rebuilt_row_and_a_file_in_a_later_example_row()
    {
        string customer = null!;
        this.Given(x => x.GivenAFileNamedAfter(customer))
            .Then(x => x.ThenTheOrderIsConfirmed())
            .WithExamples(new ExampleTable("customer") { "alice", "bob" })
            .BDDfy();
    }
}

// Fix 5: every reported step did not run, so the rows pair by position, and two hidden steps ran.
public class ReviewTwoHiddenSetups
{
    private readonly Driver _driver = new();
    private int _ran;

    private void Hidden(string name)
    {
        Files.Attach(name);
        if (++_ran == 2)
            throw new InvalidOperationException("the second hidden step failed");
    }

    public void EstablishContext() => Hidden("rv2-context-file");
    public void Setup() => Hidden("rv2-setup-file");
    public void GivenARegisteredCustomer() => _driver.A_customer_is_registered();

    [Fact]
    public void RV2_two_hidden_setups_the_second_throws() => this.BDDfy();
}

/// <summary>A consumer's own executor set before Kronikol's, so Kronikol's goes around it.</summary>
public sealed class ReviewInnerExecutor(IStepExecutor inner) : IStepExecutor
{
    public object Execute(Step step, object testObject)
    {
        if (step.Title.Contains("inner blocks", StringComparison.OrdinalIgnoreCase))
        {
            var result = inner.Execute(step, testObject);
            if (result is Task task)
                task.GetAwaiter().GetResult();
            return null!;
        }

        if (step.Title.Contains("inner detaches", StringComparison.OrdinalIgnoreCase))
        {
            inner.Execute(step, testObject);
            return Task.CompletedTask;
        }

        return inner.Execute(step, testObject);
    }
}

public class ReviewInnerExecutors
{
    private readonly AsyncDriver _driver = new();
    private readonly Driver _sync = new();
    private void GivenARegisteredCustomer() => _sync.A_customer_is_registered();
    private void ThenTheOrderIsConfirmed() => _sync.The_order_is_read_back();
    private async Task WhenInnerBlocksStepRuns() => await _driver.Inner_one();
    private async Task WhenInnerBlocksStepFails()
    {
        await _driver.Inner_one();
        throw new InvalidOperationException("inner blocks and it fails");
    }

    [Fact]
    public void RV23_an_inner_executor_that_blocks() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenInnerBlocksStepRuns()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    [Fact]
    public void RV24_an_inner_executor_that_blocks_and_the_step_fails() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenInnerBlocksStepFails()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();
}

public class ReviewAsync
{
    private readonly AsyncDriver _driver = new();
    private readonly Driver _sync = new();

    private void GivenARegisteredCustomer() => _sync.A_customer_is_registered();
    private void ThenTheOrderIsConfirmed() => _sync.The_order_is_read_back();

    private async Task<int> WhenAValueIsRead()
    {
        await _driver.PostAsync("value-before");
        await _driver.Inner_one();
        Files.Attach("rv4-file");
        return 1;
    }

    private Task<string> WhenAValueIsReadAtOnce()
    {
        _sync.Post("value-now");
        return Task.FromResult("x");
    }

    // Fix 1: Task<T>, suspending and not.
    [Fact]
    public void RV4_a_step_returning_a_task_of_a_value() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenAValueIsRead()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    [Fact]
    public void RV4b_a_step_returning_a_finished_task_of_a_value() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenAValueIsReadAtOnce()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    private async Task WhenDroppedStepRuns()
    {
        Files.Attach("rv5-before-file");
        await Task.Delay(300);
        _sync.Post("dropped");
    }

    // Fix 1: a consumer's executor around this one that hands BDDfy nothing back.
    [Fact]
    public void RV5_an_outer_executor_that_drops_the_task()
    {
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenDroppedStepRuns()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();
        Thread.Sleep(600);
    }

    private async Task WhenTimedOutStepHangs()
    {
        await _driver.Inner_one();
        Files.Attach("rv6-file");
        await new TaskCompletionSource().Task;
    }

    // Fix 1: a consumer's executor around this one with a time limit, on a step that hangs.
    [Fact]
    public void RV6_an_outer_time_limit_on_a_step_that_hangs() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenTimedOutStepHangs()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    private async Task WhenWaitsItselfStepRuns()
    {
        await _driver.Inner_one();
    }

    // Fix 1: a consumer's executor around this one that waits itself and returns nothing.
    [Fact]
    public void RV7_an_outer_executor_that_waits_itself() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenWaitsItselfStepRuns()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    private async Task WhenItNeverSuspends()
    {
        _sync.Post("no-suspend");
        await Task.CompletedTask;
    }

    private async Task WhenItThrowsWithoutSuspending()
    {
        _sync.Post("throws-no-suspend");
        await Task.CompletedTask;
        throw new InvalidOperationException("thrown with no suspend");
    }

    [Fact]
    public void RV12_a_step_that_never_suspends() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenItNeverSuspends()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    [Fact]
    public void RV13_a_step_that_throws_without_suspending() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenItThrowsWithoutSuspending()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    private async Task WhenItIsNotImplemented()
    {
        await Task.Yield();
        throw new NotImplementedException("not yet");
    }

    private async Task WhenItIsCancelled()
    {
        await Task.Yield();
        throw new OperationCanceledException("cancelled by the suite");
    }

    [Fact]
    public void RV14_an_async_step_not_implemented() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenItIsNotImplemented()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    [Fact]
    public void RV15_an_async_step_cancelled() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenItIsCancelled()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    private async Task WhenTwoRunTogether() => await Task.WhenAll(_driver.Inner_one(), _driver.Inner_two());

    [Fact]
    public void RV16_an_async_step_running_two_step_methods_together() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenTwoRunTogether()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    private async Task WhenOneIsLeftRunning()
    {
        _ = _sync.A_slow_background_check();
        await _driver.PostAsync("left-running");
        await _driver.Inner_two();
    }

    [Fact]
    public void RV17_an_async_step_leaving_a_step_method_running()
    {
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenOneIsLeftRunning()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();
        Thread.Sleep(400);
    }

    private string _customer = null!;

    private async Task GivenTheCustomerIsNamedLater(string customer)
    {
        await _driver.PostAsync("named/" + customer);
        await _driver.Inner_one();
    }

    [Fact]
    public void RV9_an_async_step_in_an_example_table()
    {
        string customer = null!;
        this.Given(x => x.GivenTheCustomerIsNamedLater(customer))
            .Then(x => x.ThenTheOrderIsConfirmed())
            .WithExamples(new ExampleTable("customer") { "alice", "bob" })
            .BDDfy();
    }
}

public class ReviewInnerScenario
{
    private readonly Driver _driver = new();

    private void GivenTheInnerStepRuns()
    {
        _driver.A_customer_is_registered();
        Files.Attach("rv8-inner-file");
    }

    private void ThenTheInnerStepEnds() => _driver.The_order_is_read_back();
    public void Run() => this.Given(x => x.GivenTheInnerStepRuns()).Then(x => x.ThenTheInnerStepEnds()).BDDfy("RV8 inner");
}

// Fix 1 with fix 3: a story run in an async step of another, after its first await.
public class ReviewOuterAsyncScenario
{
    private readonly AsyncDriver _driver = new();
    private readonly Driver _sync = new();

    private async Task GivenAnInnerScenarioRunsLater()
    {
        await _driver.PostAsync("outer-before");
        Files.Attach("rv8-outer-file");
        new ReviewInnerScenario().Run();
        await _driver.PostAsync("outer-after");
    }

    private void ThenTheOuterStepEnds() => _sync.Reference_data_is_seeded();

    [Fact]
    public void RV8_a_scenario_inside_an_async_step() =>
        this.Given(x => x.GivenAnInnerScenarioRunsLater()).Then(x => x.ThenTheOuterStepEnds()).BDDfy("RV8 outer");
}

// Fix 3: the OR2 shape, where the first Then fails, so the frames that ran still read in the list's order.
public class ReviewWhenAfterThen
{
    private readonly Driver _driver = new();
    private void GivenTheStart() => _driver.A_customer_is_registered();
    private void WhenOne() => _driver.Post("w1");
    private void ThenOneFails() { _driver.Post("t1"); throw new InvalidOperationException("then one failed"); }
    private void WhenTwo() => _driver.Post("w2");
    private void ThenTwo() => _driver.Post("t2");

    [Fact]
    public void RV10_the_first_then_fails_with_a_when_and_a_then_left() =>
        this.Given(x => x.GivenTheStart()).When(x => x.WhenOne()).Then(x => x.ThenOneFails()).When(x => x.WhenTwo()).Then(x => x.ThenTwo()).BDDfy();
}

// Fix 5: HS2's own shape (a rebuilt row holds a file) with a hidden setup ahead of it, so the rows and the frames are one apart.
public class ReviewRebuiltRowBehindASetup
{
    private readonly Driver _driver = new();
    public void Setup() => _driver.Reference_data_is_seeded();
    public void GivenARegisteredCustomer() => _driver.A_customer_is_registered();
    public void ThenTheOrderIsConfirmed()
    {
        _driver.The_order_is_read_back();
        Files.Attach("rv1b-rebuilt-row-file");
    }

    [Fact]
    public void RV1b_a_hidden_setup_and_a_rebuilt_row_with_a_file() => this.BDDfy();
}

// Fix 1: a retrying runner over an async step (Z12's shape).
public class ReviewAsyncRetries
{
    private readonly AsyncDriver _driver = new();
    private readonly Driver _sync = new();
    private static int _attempts;

    private void GivenARegisteredCustomer() => _sync.A_customer_is_registered();
    private async Task WhenAFlakyStepRuns()
    {
        await _driver.Inner_one();
        if (Interlocked.Increment(ref _attempts) == 1)
            throw new InvalidOperationException("first attempt fails");
    }
    private void ThenTheOrderIsReadBack() => _sync.The_order_is_read_back();

    [Fact]
    public void RV25_a_test_that_passes_on_its_second_attempt_over_an_async_step()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenAFlakyStepRuns()).Then(x => x.ThenTheOrderIsReadBack()).BDDfy();
                return;
            }
            catch when (attempt < 3)
            {
            }
        }
    }
}

// Fix 1: a failed Then does not stop the next Then, so what runs after a step that never closes opens under it.
public class ReviewHangThenMore
{
    private readonly AsyncDriver _driver = new();
    private readonly Driver _sync = new();
    private void GivenARegisteredCustomer() => _sync.A_customer_is_registered();
    private async Task ThenTimedOutStepHangs()
    {
        await _driver.Inner_one();
        await new TaskCompletionSource().Task;
    }
    private void ThenTheOrderIsConfirmed()
    {
        _sync.The_order_is_read_back();
        Files.Attach("rv6b-later-step-file");
    }

    [Fact]
    public void RV6b_a_then_over_the_time_limit_and_a_then_after_it() =>
        this.Given(x => x.GivenARegisteredCustomer()).Then(x => x.ThenTimedOutStepHangs()).And(x => x.ThenTheOrderIsConfirmed()).BDDfy();
}
