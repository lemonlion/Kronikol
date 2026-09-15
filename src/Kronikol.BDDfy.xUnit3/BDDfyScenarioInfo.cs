namespace Kronikol.BDDfy.xUnit3;

/// <summary>
/// Contains complete metadata for a BDDfy scenario execution, including story context, steps, tags, and results.
/// </summary>
public record BDDfyScenarioInfo
{
    public required string TestId { get; init; }
    public string? BDDfyScenarioId { get; init; }
    public required string StoryTitle { get; init; }
    public string? StoryDescription { get; init; }
    public required string ScenarioTitle { get; init; }
    public required string[] Tags { get; init; }
    public required List<BDDfyStepInfo> Steps { get; init; }
    public required TestStack.BDDfy.Result Result { get; init; }
    public TimeSpan Duration { get; init; }

    /// <summary>When the scenario finished, stamped by the processor that collects it once BDDfy has run it.</summary>
    public DateTimeOffset? EndedAt { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorStackTrace { get; init; }

    /// <summary>
    /// Raw test method arguments captured from the underlying framework's test context.
    /// When available, enables rich sub-table rendering for complex objects.
    /// </summary>
    public object?[]? RawArguments { get; init; }

    /// <summary>
    /// Parameter names corresponding to <see cref="RawArguments"/>, extracted from the test method's signature.
    /// </summary>
    public string[]? ParameterNames { get; init; }

    /// <summary>
    /// The raw test method name (e.g. "My_test_method"), used to compute OutlineId for
    /// parameterized scenario grouping. Using the raw method name avoids issues where
    /// truncated generic type notation in scenario titles breaks bracket-based base name extraction.
    /// </summary>
    public string? TestMethodName { get; init; }
}

/// <summary>
/// Captures information about an individual BDDfy step execution, including its keyword, text, result, and duration.
/// </summary>
public record BDDfyStepInfo(string Keyword, string Text, TestStack.BDDfy.Result Result = TestStack.BDDfy.Result.NotExecuted, TimeSpan? Duration = null);