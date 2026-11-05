namespace Kronikol.ReqNRoll;

/// <summary>
/// Provides constants used by the Reqnroll integration for scenario identification and tag-based classification.
/// </summary>
public static class ReqNRollConstants
{
    public const string ScenarioRuntimeIdKey = "Kronikol.RuntimeId";
    public const string StepsCollectionKey = "Kronikol.Steps";
    public const string HappyPathTag = "happy-path";
    public const string EndpointTagPrefix = "endpoint:";
    public const string StepStopwatchKey = "Kronikol.StepStopwatch";
    public const string OwnerHooksKey = "Kronikol.OwnerHooks";

    /// <summary>The running row's name, as <c>BeforeScenario</c> resolved it (#101).</summary>
    internal const string RowNameKey = "Kronikol.RowName";

    /// <summary>A step under way: on its own step context, and on the scenario's for the innermost, as a step definition can run a step.</summary>
    internal const string OpenStepKey = "Kronikol.OpenStep";
}