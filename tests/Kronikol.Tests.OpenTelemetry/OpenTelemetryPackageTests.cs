using Kronikol.Tests.Packaging;

namespace Kronikol.Tests.OpenTelemetry;

/// <summary>
/// What the packed <c>Kronikol.Extensions.OpenTelemetry</c> asks its consumers for: a measured floor for each
/// dependency, not the newest release of the day it was packed (<c>plans/CLIENT_FLOORS_PLAN.md</c> R2c). This packs the
/// project and holds its nuspec to <see cref="FloorLedger"/>, which gives each floor's evidence. Moving a floor is a
/// decision: run the suite on the new one first (<c>-p:FloorRun=true</c>), then move the csproj and the ledger
/// together.
/// </summary>
public class OpenTelemetryPackageTests
{
    [Fact]
    public void The_packed_project_asks_for_its_measured_floors() =>
        Assert.Empty(PackedNuspec.DisagreementsWithLedger("Kronikol.Extensions.OpenTelemetry"));
}
