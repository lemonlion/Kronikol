using System.Text.Json;
using Kronikol.Reports;

namespace Kronikol.Tests.Parties;

/// <summary>
/// #190 R1: <c>Failures.jsonl</c>'s header carries the run's <c>serviceId</c> beside <c>suite</c>, only when the run
/// declared one, so a script reading failures from several services' runs can tell them apart (plans/PARTY_IDENTITY_PLAN.md §5.8).
/// </summary>
public class PartiesDigestTests
{
    private static JsonElement Header(string? serviceId)
    {
        var digest = FailuresDigestGenerator.Generate(PartiesBlockTests.Features(), PartiesBlockTests.Logs(), "TestRunReport.html", "5.14.0",
            queryScript: false, suite: "Suite", serviceId: serviceId);
        using var header = JsonDocument.Parse(digest.Jsonl.Split('\n')[0]);
        return header.RootElement.Clone();
    }

    [Fact]
    public void The_header_carries_a_declared_service_id_and_no_member_otherwise()
    {
        Assert.Equal("breakfast-provider", Header("breakfast-provider").GetProperty("serviceId").GetString());
        Assert.False(Header(null).TryGetProperty("serviceId", out _));
        Assert.Equal("Suite", Header(null).GetProperty("suite").GetString());
    }
}
