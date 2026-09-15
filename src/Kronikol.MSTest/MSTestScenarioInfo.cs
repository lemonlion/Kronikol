using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kronikol.MSTest;

/// <summary>
/// Captures metadata for an MSTest scenario execution, including class name, method name, outcome, and error details.
/// </summary>
public record MSTestScenarioInfo
{
    public required string TestClassSimpleName { get; init; }
    public required string TestMethodName { get; init; }
    public string? TestDisplayName { get; init; }
    public required string TestId { get; init; }
    public required UnitTestOutcome Outcome { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorStackTrace { get; init; }

    /// <summary>The exception type name, when the test failed. A category, never a cause.</summary>
    public string? FailureCause { get; init; }
    public string? Endpoint { get; init; }
    public bool IsHappyPath { get; init; }
    public TimeSpan? Duration { get; init; }

    /// <summary>When the test finished, stamped in the cleanup that collects it.</summary>
    public DateTimeOffset? EndedAt { get; init; }
    public string?[]? ParameterNames { get; init; }
}