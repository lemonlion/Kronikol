using System.Reflection;
using System.Text;
using Io.Cucumber.Messages.Types;
using Reqnroll;
using Reqnroll.Formatters.RuntimeSupport;

namespace Shapes.Suite.Hooks;

/// <summary>
/// What Reqnroll knows at run time, written to the file named by KRK_PICKLE_PROBE, through the same internal members the adapter reads:
/// for each scenario the pickle PickleIdIndex finds, how many leading pickle steps come from a Background, and what StepInfo.PickleStepId holds.
/// </summary>
[Binding]
public class PickleProbe(ScenarioContext scenario, FeatureContext feature)
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly object Gate = new();

    private static void Write(string line)
    {
        if (Environment.GetEnvironmentVariable("KRK_PICKLE_PROBE") is not { Length: > 0 } path)
            return;
        lock (Gate)
            File.AppendAllText(path, line + "\n", new UTF8Encoding(false));
    }

    private static IEnumerable<string> BackgroundStepIds(GherkinDocument? document)
    {
        foreach (var child in document?.Feature?.Children ?? [])
        {
            foreach (var step in child.Background?.Steps ?? [])
                yield return step.Id;
            foreach (var ruleChild in child.Rule?.Children ?? [])
                foreach (var step in ruleChild.Background?.Steps ?? [])
                    yield return step.Id;
        }
    }

    [BeforeScenario(Order = int.MinValue + 1)]
    public void Scenario()
    {
        if (Environment.GetEnvironmentVariable("KRK_PICKLE_PROBE") is not { Length: > 0 })
            return;
        var messages = typeof(FeatureInfo).GetProperty("FeatureCucumberMessages", Any)?.GetValue(feature.FeatureInfo) as IFeatureLevelCucumberMessages;
        var info = scenario.ScenarioInfo;
        var pickleId = typeof(ScenarioInfo).GetProperty("PickleId", Any)?.GetValue(info) as string;
        var index = typeof(ScenarioInfo).GetProperty("PickleIdIndex", Any)?.GetValue(info) as string;
        var pickles = messages?.Pickles?.ToList() ?? [];
        var pickle = int.TryParse(index, out var i) && i >= 0 && i < pickles.Count ? pickles[i] : null;
        var background = BackgroundStepIds(messages?.GherkinDocument).ToHashSet();
        var leading = pickle?.Steps.TakeWhile(s => s.AstNodeIds.Count > 0 && background.Contains(s.AstNodeIds[0])).Count();
        var total = pickle?.Steps.Count;
        var anyLater = pickle?.Steps.Skip(leading ?? 0).Count(s => s.AstNodeIds.Count > 0 && background.Contains(s.AstNodeIds[0]));
        Write($"SCENARIO\t{feature.FeatureInfo.Title}\t{info.Title}\thasMessages={messages?.HasMessages}\tindex={index}\tpickleId={(string.IsNullOrEmpty(pickleId) ? "<empty>" : "set")}\tpickleName={pickle?.Name}\tpickleSteps={total}\tleadingBackground={leading}\tlaterBackground={anyLater}");
    }

    [BeforeStep(Order = int.MinValue + 1)]
    public void Step()
    {
        if (Environment.GetEnvironmentVariable("KRK_PICKLE_PROBE") is not { Length: > 0 })
            return;
        var stepInfo = scenario.StepContext.StepInfo;
        var pickleStepId = typeof(StepInfo).GetProperty("PickleStepId", Any)?.GetValue(stepInfo) as string;
        var sequence = typeof(ScenarioInfo).GetProperty("PickleStepSequence", Any)?.GetValue(scenario.ScenarioInfo);
        Write($"STEP\t{scenario.ScenarioInfo.Title}\tkeyword={stepInfo.StepInstance.StepDefinitionKeyword}\ttype={stepInfo.StepDefinitionType}\ttext={stepInfo.Text}\tpickleStepId={(string.IsNullOrEmpty(pickleStepId) ? "<empty>" : "set")}\tsequence={(sequence is null ? "null" : "set")}");
    }
}
