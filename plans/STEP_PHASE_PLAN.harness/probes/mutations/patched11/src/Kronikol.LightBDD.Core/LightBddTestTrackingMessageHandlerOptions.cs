using LightBDD.Core.ExecutionContext;
using LightBDD.Core.Metadata;
using Kronikol.Tracking;

namespace Kronikol.LightBDD;

/// <summary>
/// LightBDD-specific configuration options for the test tracking message handler.
/// </summary>
public record LightBddTestTrackingMessageHandlerOptions : TestTrackingMessageHandlerOptions
{
    /// <summary>
    /// A reusable delegate that returns the current LightBDD scenario's name and runtime ID.
    /// Use this when configuring extension options (e.g. <c>SqlTrackingInterceptorOptions</c>)
    /// instead of writing the fetcher lambda inline.
    /// </summary>
    public static readonly Func<(string Name, string Id)> TestInfoFetcher = CurrentTestInfo.Fetcher;

    public LightBddTestTrackingMessageHandlerOptions()
    {
        CurrentTestInfoFetcher = CurrentTestInfo.Fetcher;
        CurrentStepTypeFetcher = GetTopLevelStepType;
    }

    private static string? GetTopLevelStepType()
    {
        var step = ScenarioExecutionContext.CurrentStep;
        if (step is null)
            return null;

        var info = step.Info;
        while (info.Parent is IStepInfo parentStep)
            info = parentStep;

        var stepType = info.Name?.StepTypeName?.Name;
        // Only for a suite with no step decorator (ReportWritersConfiguration's CreateStandardReportsWithDiagrams registers
        // none): where the decorator runs, the phase is the test's, and a top-level AND is not a setup step.
        if (!StepCollector.FrameworkStepsSetPhase())
            TestPhaseContext.Current = PhaseConfiguration.ResolvePhaseFromStepType(stepType);
        return stepType;
    }
}