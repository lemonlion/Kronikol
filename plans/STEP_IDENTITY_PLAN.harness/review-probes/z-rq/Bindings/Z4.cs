using Reqnroll;

namespace HookSteps.Repro;

[Binding]
public class Z4Hooks
{
    // What a suite does: a log or a screenshot after every step.
    [AfterStep("attach-always")]
    public void Attach(IReqnrollOutputHelper output)
    {
        var path = Path.Combine(Path.GetTempPath(), "z-step-log-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "a log");
        output.AddAttachment(path);
    }
}
