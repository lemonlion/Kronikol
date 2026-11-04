using Reqnroll;

public class HookBase
{
    protected static void Note(string what) => File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "hooks.txt"), what + "\n");

    [AfterScenario(Order = int.MinValue)] public void BasePublic() => Note("base public");
    [AfterScenario(Order = int.MinValue)] internal void BaseInternal() => Note("base internal");
    [AfterScenario(Order = int.MinValue)] private protected void BasePrivateProtected() => Note("base private protected");
    [AfterScenario(Order = int.MinValue)] private void BasePrivate() => Note("base private");
    [AfterScenario(Order = int.MinValue)] protected void BaseProtected() => Note("base protected");
}

[Binding]
public class HookDerived : HookBase
{
    [AfterScenario(Order = int.MinValue)] public void OwnPublic() => Note("own public");
    [AfterScenario(Order = int.MinValue)] internal void OwnInternal() => Note("own internal");
    [AfterScenario(Order = int.MinValue)] private void OwnPrivate() => Note("own private");
    [Given("a step")] public void AStep() => Note("step");
}
