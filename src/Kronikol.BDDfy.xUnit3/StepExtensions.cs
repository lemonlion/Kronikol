using TestStack.BDDfy;

namespace Kronikol.BDDfy.xUnit3;

internal static class StepExtensions
{
    private const string XunitSkipMarker = "$XunitDynamicSkip$";

    private static readonly string[] Keywords = ["Given", "When", "Then", "And", "But"];

    /// <summary>What xUnit's <c>Assert.Skip</c> gave as its reason, if that is what this message is.</summary>
    internal static string? SkipReasonIn(string? message) =>
        message?.StartsWith(XunitSkipMarker, StringComparison.Ordinal) == true ? message[XunitSkipMarker.Length..] : null;

    public static BDDfyStepInfo ToBDDfyStepInfo(this Step step)
    {
        var title = step.Title.Trim();
        var firstSpace = title.IndexOf(' ');
        var duration = step.Duration != TimeSpan.Zero ? step.Duration : (TimeSpan?)null;

        if (firstSpace > 0)
        {
            var firstWord = title[..firstSpace];
            if (Keywords.Any(k => k.Equals(firstWord, StringComparison.OrdinalIgnoreCase)))
                return new BDDfyStepInfo(firstWord, title[(firstSpace + 1)..], step.Result, duration) { StepId = step.Id, SkipReason = SkipReasonIn(step.Exception?.Message) };
        }

        return new BDDfyStepInfo(WordOf(step.ExecutionOrder), title, step.Result, duration) { StepId = step.Id, SkipReason = SkipReasonIn(step.Exception?.Message) };
    }

    // A title with no keyword in it, as BDDfy's attributes write one, reads by the kind of step it is.
    private static string WordOf(ExecutionOrder order) => order switch
    {
        ExecutionOrder.SetupState => "Given",
        ExecutionOrder.Transition => "When",
        ExecutionOrder.Assertion => "Then",
        ExecutionOrder.ConsecutiveSetupState or ExecutionOrder.ConsecutiveTransition or ExecutionOrder.ConsecutiveAssertion => "And",
        _ => "Step",
    };
}
