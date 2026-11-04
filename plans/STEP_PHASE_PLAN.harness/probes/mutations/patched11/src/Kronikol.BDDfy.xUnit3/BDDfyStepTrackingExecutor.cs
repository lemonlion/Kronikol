using System.Text.RegularExpressions;
using TestStack.BDDfy;
using TestStack.BDDfy.Configuration;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.BDDfy.xUnit3;

internal class BDDfyStepTrackingExecutor(IStepExecutor inner) : IStepExecutor
{
    private static readonly AsyncLocal<string?> CurrentStepTypeLocal = new();

    public static string? CurrentStepType => CurrentStepTypeLocal.Value;

    // Matches record-style ToString: "TypeName { Prop = Val, ... }" (possibly nested)
    private static readonly Regex RecordPattern = new(@"\b(\w+)\s*\{(?:[^{}]|\{(?:[^{}]|\{[^{}]*\})*\})*\}", RegexOptions.Compiled);

    public object Execute(Step step, object testObject)
    {
        var keyword = step.ExecutionOrder switch
        {
            ExecutionOrder.SetupState or ExecutionOrder.ConsecutiveSetupState => "Given",
            ExecutionOrder.Transition or ExecutionOrder.ConsecutiveTransition => "When",
            ExecutionOrder.Assertion or ExecutionOrder.ConsecutiveAssertion => "Then",
            _ => null
        };

        CurrentStepTypeLocal.Value = keyword?.ToUpperInvariant();

        // Resolve the xUnit test ID for StepCollector
        string? testId = null;
        try { testId = Xunit.TestContext.Current.Test?.UniqueID; } catch { /* not in xUnit context */ }

        var underKeyword = testId is not null && StepCollector.UnderOpenKeyword(testId);
        var phaseBefore = underKeyword ? TestPhaseContext.Current : TestPhase.Unknown;
        if (testId is null || (keyword is null && !underKeyword))
            TestPhaseContext.Current = PhaseConfiguration.ResolvePhaseFromStepType(keyword);

        var stepText = ExtractStepText(step.Title, keyword);
        if (testId is not null)
            StepCollector.StartStep(testId, keyword, stepText, null, null, table: null, docString: null, phaseKeyword: keyword);

        var closed = false;
        try
        {
            var result = inner.Execute(step, testObject);
            if (testId is not null && Configurator.AsyncVoidSupportEnabled && result is Task task)
            {
                // Its own flow leaves it now, and its task closes it.
                closed = true;
                StepCollector.LeaveCompletedStep(testId);
                return CloseWhenDone(testId, task);
            }
            return result;
        }
        catch (Exception)
        {
            if (testId is not null)
                StepCollector.CompleteStep(testId, passed: false);

            // The finally would close the step above it.
            closed = true;
            throw;
        }
        finally
        {
            if (!closed && testId is not null && StepCollector.HasActiveStep(testId))
                StepCollector.CompleteStep(testId, passed: true);
            CurrentStepTypeLocal.Value = null;
            TestPhaseContext.Current = phaseBefore;
        }
    }

    private static async Task CloseWhenDone(string testId, Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            StepCollector.CompleteStep(testId, passed: false);
            throw;
        }

        if (StepCollector.HasActiveStep(testId))
            StepCollector.CompleteStep(testId, passed: true);
    }

    /// <summary>
    /// Extracts the step text from BDDfy's step title, stripping the keyword prefix
    /// (e.g. "Given a user exists" → "a user exists"). Falls back to the full title
    /// if no keyword prefix is present. Also truncates complex object parameters
    /// (record-style ToString) to [TypeName] for cleaner hnote display.
    /// </summary>
    private static string ExtractStepText(string title, string? keyword)
    {
        var text = title.Trim();
        if (keyword is not null && text.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
        {
            var afterKeyword = text[keyword.Length..];
            if (afterKeyword.Length > 0 && afterKeyword[0] == ' ')
                text = afterKeyword[1..];
        }

        // Truncate record-style complex objects: "TypeName { ... }" → "[TypeName]"
        text = RecordPattern.Replace(text, m =>
        {
            var fullMatch = m.Value;
            return ParameterParser.IsComplexObjectString(fullMatch)
                ? $"[{ParameterParser.ExtractTypeNameFromComplexString(fullMatch)}]"
                : fullMatch;
        });

        return text;
    }
}
