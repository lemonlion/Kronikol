using Kronikol.Tracking;
using Reqnroll;
using Xunit;

namespace EngineFixtures;

[Binding]
public class SkippingBindings
{
    [Step] private static void Skips_itself() => Assert.Skip("not on this platform");

    [BeforeScenario("skipsitself")] public void Skip() => Skips_itself();
}
