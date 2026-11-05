using Reqnroll;

namespace EngineFixtures;

[Binding]
public class FilesHooks(ScenarioContext scenario, IReqnrollOutputHelper output)
{
    private static string File(string name, string title)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{name}-{title.Split(' ')[0]}.txt");
        System.IO.File.WriteAllText(path, "x");
        return path;
    }

    [AfterScenario("shotafter")] public void ShotAfter() => output.AddAttachment(File("after-scenario", scenario.ScenarioInfo.Title));
    [AfterStep("shotafterstep")] public void ShotAfterStep() => output.AddAttachment(File("after-step", scenario.StepContext.StepInfo.Text.Replace(' ', '_') + " " + scenario.ScenarioInfo.Title));
    [BeforeScenario("shotbefore")] public void ShotBefore() => output.AddAttachment(File("before-scenario", scenario.ScenarioInfo.Title));
}
