using LightBDD.Core.Execution;
using LightBDD.Core.ExecutionContext;
using LightBDD.Core.Extensibility.Execution;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.LightBDD;

/// <summary>
/// LightBDD step decorator that brackets each step with
/// <see cref="StepCollector.StartStep(string, string, string, string[], object[])"/> and
/// <see cref="StepCollector.CompleteStep(string, bool, string)"/>, enabling step delimiters in sequence diagrams
/// and assertion sub-step attachment during step execution.
/// Registered automatically by <c>CreateStandardReportsWithDiagrams()</c>.
/// </summary>
internal sealed class StepTrackingStepDecorator : IStepDecorator
{
    public async Task ExecuteAsync(IStep step, Func<Task> stepInvocation)
    {
        var keyword = step.Info.Name.StepTypeName?.OriginalName;
        var text = BuildTruncatedStepText(step, keyword);

        string? testId = null;
        try { testId = ScenarioExecutionContext.CurrentScenario.Info.RuntimeId.ToString(); }
        catch { /* not in scenario context */ }

        if (testId is not null)
            StepCollector.StartStep(testId, keyword, text, null, null);

        try
        {
            await stepInvocation();
        }
        catch
        {
            if (testId is not null)
                StepCollector.CompleteStep(testId, passed: false);
            throw;
        }
        finally
        {
            if (testId is not null && StepCollector.HasActiveStep(testId))
                StepCollector.CompleteStep(testId, passed: true);
        }
    }

    /// <summary>
    /// Builds step text from NameFormat, truncating complex object parameters to [TypeName]
    /// instead of embedding the full ToString() representation in the hnote.
    /// </summary>
    private static string BuildTruncatedStepText(IStep step, string? keyword)
    {
        var nameParams = step.Info.Name.Parameters.ToArray();
        if (nameParams.Length == 0)
        {
            // No params — use ToString() directly
            var text = step.Info.Name.ToString();
            if (keyword is not null && text.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
                text = text[keyword.Length..].TrimStart();
            return text;
        }

        var format = step.Info.Name.NameFormat;
        if (keyword is not null && format.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
            format = format[keyword.Length..].TrimStart();

        // Replace {N} placeholders with actual or truncated values
        for (var i = 0; i < nameParams.Length; i++)
        {
            var formattedValue = nameParams[i].FormattedValue ?? "";
            var display = ParameterParser.IsComplexObjectString(formattedValue)
                ? $"[{ParameterParser.ExtractTypeNameFromComplexString(formattedValue)}]"
                : formattedValue;
            format = format.Replace($"\"{{{i}}}\"", $"\"{display}\"");
            format = format.Replace($"{{{i}}}", display);
        }

        return format;
    }
}
