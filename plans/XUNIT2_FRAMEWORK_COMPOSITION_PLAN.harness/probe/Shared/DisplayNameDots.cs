using Xunit;
using Xunit.Abstractions;

namespace Probe;

/// <summary>
/// Display names with a dot in them. ScenarioTitleResolver.FormatScenarioDisplayName takes the text after
/// the last '.' as the method name, so the first would be called "2" and the second (nothing after its
/// '.') reaches humanized[0] on an empty string. A class of their own, so whatever they break stays here.
/// </summary>
public partial class DisplayNameDots(ITestOutputHelper output)
{
    [Fact(DisplayName = "Order API returns 404 for v1.2")]
    public Task DottedVersion() => ProbeHttp.GetAsync("/dots/version-1-2", output);

    [Fact(DisplayName = "Does the thing.")]
    public Task TrailingDot() => ProbeHttp.GetAsync("/dots/trailing-dot", output);
}
