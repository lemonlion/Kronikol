using TestStack.BDDfy;
using Xunit;

namespace EngineFixtures;

/// <summary>A test that runs more than one story or row: the test's result is what xUnit says, whichever of them failed.</summary>
[Story(AsA = "probe", IWant = "a test's result to read as xUnit's", SoThat = "a failure cannot hide behind the first story")]
public class Outcomes
{
    private int _attempts;

    private void GivenAStep() { }
    private void WhenItPasses() { }
    private void WhenTheSecondStoryFails() => throw new InvalidOperationException("the second story's failure");

    private void WhenTheFirstAttemptFails()
    {
        if (++_attempts == 1)
            throw new InvalidOperationException("the first attempt's failure");
    }

    private void GivenARow(int row) { }

    private void WhenRowTwoFails(int row)
    {
        if (row == 2)
            throw new InvalidOperationException("the second row's failure");
    }

    private void WhenTheStepSkipsTheTest() => Assert.Skip("not on this platform");
    private void ThenNothingElseRuns() => throw new InvalidOperationException("this step must not run");

    [Fact]
    public void S1_a_step_skips_the_test() =>
        this.Given(x => x.GivenAStep()).When(x => x.WhenTheStepSkipsTheTest()).Then(x => x.ThenNothingElseRuns()).BDDfy();

    [Fact]
    public void O0_one_story_passes() =>
        this.Given(x => x.GivenAStep()).When(x => x.WhenItPasses()).BDDfy();

    [Fact]
    public void O1_the_second_story_fails()
    {
        this.Given(x => x.GivenAStep()).When(x => x.WhenItPasses()).BDDfy();
        this.Given(x => x.GivenAStep()).When(x => x.WhenTheSecondStoryFails()).BDDfy();
    }

    [Fact]
    public void O2_a_story_passes_when_it_is_run_again()
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                this.Given(x => x.GivenAStep()).When(x => x.WhenTheFirstAttemptFails()).BDDfy();
                return;
            }
            catch (InvalidOperationException) when (attempt == 0)
            {
                // The way a retry helper goes round again.
            }
        }
    }

    [Fact]
    public void O3_a_later_row_fails()
    {
        var row = 0;
        this.Given(x => x.GivenARow(row)).When(x => x.WhenRowTwoFails(row))
            .WithExamples(new ExampleTable("row") { 1, 2 })
            .BDDfy();
    }
}

/// <summary>A step's time: BDDfy's stopwatch stops when a step's task comes back, at its first await.</summary>
[Story(AsA = "probe", IWant = "a step's time to be its time", SoThat = "a slow scenario reads slow")]
public class Durations
{
    private void GivenAStep() { }
    private async Task WhenItTakesAWhile() => await Task.Delay(400);
    private async Task ThenSoDoesThis() => await Task.Delay(200);

    [Fact]
    public void D1_an_async_step_takes_as_long_as_its_task() =>
        this.Given(x => x.GivenAStep()).When(x => x.WhenItTakesAWhile()).Then(x => x.ThenSoDoesThis()).BDDfy();

    [Fact]
    public void D2_two_stories_take_as_long_as_both()
    {
        this.Given(x => x.GivenAStep()).When(x => x.WhenItTakesAWhile()).BDDfy();
        this.Given(x => x.GivenAStep()).When(x => x.ThenSoDoesThis()).BDDfy();
    }
}

/// <summary>A step named by BDDfy's attributes has a title with no keyword in it.</summary>
public class StepsByAttribute
{
    [Given("a thing is there")]
    public void Arrange() { }

    [AndGiven("another is too")]
    public void ArrangeMore() { }

    [When("the thing is used")]
    public void Act() { }

    [Then("it worked")]
    public void Assert_it() { }

    [Fact]
    public void K1_steps_named_by_attributes() => this.BDDfy();
}
