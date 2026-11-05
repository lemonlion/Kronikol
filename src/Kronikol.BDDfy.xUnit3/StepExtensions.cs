using TestStack.BDDfy;

namespace Kronikol.BDDfy.xUnit3;

internal static class StepExtensions
{
    private static readonly string[] Keywords = ["Given", "When", "Then", "And", "But"];

    public static BDDfyStepInfo ToBDDfyStepInfo(this Step step)
    {
        var title = step.Title.Trim();
        var firstSpace = title.IndexOf(' ');
        var duration = step.Duration != TimeSpan.Zero ? step.Duration : (TimeSpan?)null;

        if (firstSpace > 0)
        {
            var firstWord = title[..firstSpace];
            if (Keywords.Any(k => k.Equals(firstWord, StringComparison.OrdinalIgnoreCase)))
                return new BDDfyStepInfo(firstWord, title[(firstSpace + 1)..], step.Result, duration) { StepId = step.Id };
        }

        return new BDDfyStepInfo("Step", title, step.Result, duration) { StepId = step.Id };
    }
}
