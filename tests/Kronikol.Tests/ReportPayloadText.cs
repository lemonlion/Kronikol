using System.Text.Json;
using Kronikol.Reports;

namespace Kronikol.Tests;

/// <summary>
/// A payload (a body or a diagram's PlantUML source) as <c>TestRunReport.json</c> holds it: text, or, from 4.0.0 by
/// default, <c>{"$h", "$n", "$z"}</c> in its place when that is smaller (#85,
/// <c>ReportConfigurationOptions.CompressTestRunReportPayloads</c>). Facts about what a report says read through
/// this, so they hold for either form.
/// </summary>
internal static class ReportPayloadText
{
    public static string? Of(JsonElement payload) => payload.ValueKind switch
    {
        JsonValueKind.String => payload.GetString(),
        JsonValueKind.Null => null,
        JsonValueKind.Object when payload.TryGetProperty("$z", out var z) => ReportPayloads.Inflate(z.GetString()!),
        _ => throw new InvalidOperationException($"Not a payload: {payload.ValueKind}")
    };
}
