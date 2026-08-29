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
    public const string CategoryTagPrefix = "category:";
    public const string StepStopwatchKey = "Kronikol.StepStopwatch";
    public const string OwnerHooksKey = "Kronikol.OwnerHooks";
}