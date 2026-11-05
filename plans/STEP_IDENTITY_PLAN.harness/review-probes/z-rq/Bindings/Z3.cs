using Reqnroll;
using Reqnroll.UnitTestProvider;

namespace HookSteps.Repro;

[Binding]
public class Z3Hooks(IUnitTestRuntimeProvider runtime)
{
    // What a suite does: a scenario that needs something this machine lacks skips itself.
    [BeforeScenario("needs-gpu")] public void NeedsGpu() => runtime.TestIgnore("no GPU here");
}
