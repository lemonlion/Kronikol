using Kronikol.Tracking;
using Reqnroll;
using Xunit;

namespace EngineFixtures;

[Binding]
public class SkippingBindings
{
    [Step] private static void Skips_itself() => Assert.Skip("not on this platform");

    [BeforeScenario("skipsitself")] public void Skip() => Skips_itself();

    [Step] private static void Fails_in_a_hook() => throw new InvalidOperationException("the hook's step method failed");

    [When("a step skips itself")] public void SkipsItself() => Assert.Skip("not on this platform");

    [BeforeScenario("catchesfailedstep")]
    public void Catches()
    {
        try
        {
            Fails_in_a_hook();
        }
        catch (InvalidOperationException)
        {
            // What this hook is for.
        }
    }

    [AfterScenario("stepfailsafter")] public void FailsAfter() => Fails_in_a_hook();
}
