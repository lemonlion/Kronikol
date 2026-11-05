using System.Collections.Generic;

namespace Kronikol.StepTracking;

/// <summary>
/// Result of the step weaving operation.
/// </summary>
public class WeaveResult
{
    public int WeavedCount { get; set; }
    public string? SkipReason { get; set; }

    /// <summary>The step methods left as the compiler wrote them, each with why: they run, without a step.</summary>
    public List<UnwovenStep> UnwovenSteps { get; } = new List<UnwovenStep>();

    /// <summary>Methods with a step attribute and no body: abstract, an interface's, extern. Nothing to bracket, and nothing wrong: an override
    /// that wants a step carries the attribute itself.</summary>
    public List<UnwovenStep> StepsWithNoBody { get; } = new();
}

/// <summary>A step method the weaver left as the compiler wrote it, and why.</summary>
public sealed class UnwovenStep
{
    public UnwovenStep(string method, string? document, int line, string reason)
    {
        Method = method;
        Document = document;
        Line = line;
        Reason = reason;
    }

    public string Method { get; }
    public string? Document { get; }
    public int Line { get; }
    public string Reason { get; }

    public override string ToString() =>
        Document == null ? $"{Method}: {Reason}" : $"{Document}, line {Line}, in {Method}: {Reason}";
}
