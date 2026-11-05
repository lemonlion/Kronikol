using Kronikol.Tracking;
using TestStack.BDDfy;
using Xunit;

namespace HookSteps.Bddfy;

// Each path says which shape and which attempt, so a read says where each call landed.

public class HiddenMiddle
{
    private readonly Driver _d = new();
    [Given("a registered customer")] public void A() => _d.Post("h2/a");
    [AndGiven("something quiet happens", ShouldReport = false)] public void Quiet() => _d.Post("h2/hidden");
    [When("the order is submitted")] public void B() => _d.Post("h2/b");
    [Then("it is confirmed")] public void C() => _d.Post("h2/c");
    [Fact] public void H2_a_hidden_step_between_rows() => this.BDDfy();
}

public class HiddenHoldsTheNextRowsText
{
    private readonly Driver _d = new();
    [Given("a registered customer")] public void A() => _d.Post("h3/a");
    [AndGiven("the order is submitted to the audit log", ShouldReport = false)] public void Quiet() => _d.Post("h3/hidden");
    [When("the order is submitted")] public void B() => _d.Post("h3/b");
    [Then("it is confirmed")] public void C() => _d.Post("h3/c");
    [Fact] public void H3_a_hidden_step_whose_title_holds_the_next_rows_text() => this.BDDfy();
}

public class HiddenFirstHoldsTheFirstRowsText
{
    private readonly Driver _d = new();
    [Given("the stock is checked in the warehouse", ShouldReport = false)] public void Quiet() => _d.Post("h3b/hidden");
    [AndGiven("the stock is checked")] public void A() => _d.Post("h3b/a");
    [Then("it is confirmed")] public void C() => _d.Post("h3b/c");
    [Fact] public void H3b_a_hidden_first_step_whose_title_holds_the_first_rows_text() => this.BDDfy();
}

public class TearDownSharesARowsText
{
    private readonly Driver _d = new();
    private void GivenACustomer() => _d.Post("h4/a");
    private void WhenItFails() { _d.Post("h4/fail"); throw new InvalidOperationException("fails"); }
    private void CleanUpTheBasket() => _d.Post("h4/clean");

    [Fact]
    public void H4_a_teardown_with_the_text_of_a_row_that_never_ran() =>
        this.Given(x => x.GivenACustomer()).When(x => x.WhenItFails()).And(x => x.CleanUpTheBasket()).TearDownWith(x => x.CleanUpTheBasket()).BDDfy();
}

public class TearDownAfterAFailure
{
    private readonly Driver _d = new();
    private void GivenACustomer() => _d.Post("h5/a");
    private void WhenItFails() { _d.Post("h5/fail"); throw new InvalidOperationException("fails"); }
    private void ThenAThing() => _d.Post("h5/then");
    private void Cleanup() { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "h5-teardown-ran.txt"), "ran"); _d.Post("h5/teardown"); }

    [Fact]
    public void H5_a_teardown_after_a_failure() =>
        this.Given(x => x.GivenACustomer()).When(x => x.WhenItFails()).Then(x => x.ThenAThing()).TearDownWith(x => x.Cleanup()).BDDfy();
}

public class SetupFails
{
    private readonly Driver _d = new();
    public void Setup() { _d.Post("h6/setup"); throw new InvalidOperationException("setup fails"); }
    public void GivenARegisteredCustomer() => _d.Post("h6/a");
    public void ThenTheOrderIsConfirmed() => _d.Post("h6/c");
    [Fact] public void H6_a_setup_that_fails() => this.BDDfy();
}

public class HiddenOnly
{
    private readonly Driver _d = new();
    public void Setup() => _d.Post("h7/setup");
    public void TearDown() => _d.Post("h7/teardown");
    public void GivenAThing() => _d.Post("h7/a");
    [Fact] public void H7_setup_one_row_and_teardown() => this.BDDfy();
}

public class ExampleRepeats
{
    private readonly Driver _d = new();
    private static int _b;
    private string _who = "";
    private void AddItem(string who) { _who = who; _d.Post("l1/add-" + who); }
    private void CheckOut()
    {
        _d.Post("l1/checkout-" + _who);
        if (_who == "alice") throw new InvalidOperationException("alice is refused");
    }

    [Fact]
    public void L1_examples_rows_repeat_and_the_first_example_stops_at_the_second()
    {
        string who = null!;
        this.Given(x => x.AddItem(who)).And(x => x.CheckOut()).And(x => x.AddItem(who)).And(x => x.CheckOut())
            .WithExamples(new ExampleTable("who") { "alice", "bob" })
            .BDDfy();
    }
}

public class RetryRepeats
{
    private readonly Driver _d = new();
    private static int _attempt;
    private static int _fail;
    private void AddItem() => _d.Post("l2/add-" + _attempt);
    private void CheckOut()
    {
        _d.Post("l2/checkout-" + _attempt);
        if (Interlocked.Increment(ref _fail) == 1) throw new InvalidOperationException("first checkout fails");
    }

    private static void Attempts(int times, Action test)
    {
        for (var attempt = 1; ; attempt++)
        {
            _attempt = attempt;
            try { test(); return; }
            catch when (attempt < times) { }
        }
    }

    [Fact]
    public void L2_a_retry_whose_rows_repeat_and_the_first_attempt_stops_at_the_second() => Attempts(3, () =>
        this.Given(x => x.AddItem()).And(x => x.CheckOut()).And(x => x.AddItem()).And(x => x.CheckOut()).BDDfy());
}

public class RetryPlain
{
    private readonly Driver _d = new();
    private static int _attempt;
    private static int _fail;
    private void GivenACustomer() => _d.Post("l3/given-" + _attempt);
    private void WhenAFlakyStepRuns()
    {
        _d.Post("l3/when-" + _attempt);
        if (Interlocked.Increment(ref _fail) == 1) throw new InvalidOperationException("first attempt fails");
    }
    private void ThenItIsRead() => _d.Post("l3/then-" + _attempt);

    private static void Attempts(int times, Action test)
    {
        for (var attempt = 1; ; attempt++)
        {
            _attempt = attempt;
            try { test(); return; }
            catch when (attempt < times) { }
        }
    }

    [Fact]
    public void L3_a_retry_passes_on_its_second_attempt() => Attempts(3, () =>
        this.Given(x => x.GivenACustomer()).When(x => x.WhenAFlakyStepRuns()).Then(x => x.ThenItIsRead()).BDDfy());
}

public class TwoRunsPassing
{
    private readonly Driver _d = new();
    private void GivenACustomer() => _d.Post("l4/given");
    private void ThenItIsRead() => _d.Post("l4/then");
    private void WhenAnOrder() => _d.Post("l4/when");

    [Fact]
    public void L4_two_bddfy_calls_in_one_test()
    {
        this.Given(x => x.GivenACustomer()).Then(x => x.ThenItIsRead()).BDDfy("first of two");
        this.When(x => x.WhenAnOrder()).BDDfy("second of two");
    }
}

public class ExamplesPassing
{
    private readonly Driver _d = new();
    private string _who = "";
    private void GivenTheCustomerIsNamed(string who) { _who = who; _d.Post("l5/given-" + who); }
    private void ThenTheOrderIsConfirmed() => _d.Post("l5/then-" + _who);

    [Fact]
    public void L5_examples_all_pass()
    {
        string who = null!;
        this.Given(x => x.GivenTheCustomerIsNamed(who)).Then(x => x.ThenTheOrderIsConfirmed())
            .WithExamples(new ExampleTable("who") { "alice", "bob" })
            .BDDfy();
    }
}

public class AWovenStepLeftOpen
{
    private readonly Driver _d = new();
    private static readonly TaskCompletionSource Never = new();
    [Step] public async Task Waits_for_ever() { _d.Post("o1/open-before"); await Never.Task; }
    private void GivenACustomer() => _d.Post("o1/given");
    private void ThenItIsRead() => _d.Post("o1/then");

    [Fact]
    public void O1_a_step_method_after_the_steps_still_open_at_report_time()
    {
        this.Given(x => x.GivenACustomer()).Then(x => x.ThenItIsRead()).BDDfy();
        _ = Waits_for_ever();
        _d.Post("o1/open-after");
    }
}
