using Kronikol.Tests.Packaging;

namespace Kronikol.Tests.MediatR;

/// <summary>
/// What the packed <c>Kronikol.Extensions.MediatR</c> asks its consumers for: a measured floor for each dependency, not
/// the newest release of the day it was packed (<c>plans/CLIENT_FLOORS_PLAN.md</c> R2c). This packs the project and
/// holds its nuspec to <see cref="FloorLedger"/>, which gives each floor's evidence. Moving a floor is a decision: run
/// the suite on the new one first (<c>-p:FloorRun=true</c>), then move the csproj and the ledger together.
/// </summary>
public class MediatRPackageTests
{
    /// <summary>MediatR's own pin, 14.1.0, is R3b's to measure, so until then the packed project disagrees with the
    /// ledger in that one line and in no other; R3b makes this an empty list.</summary>
    [Fact]
    public void The_packed_project_asks_for_its_measured_floors() =>
        Assert.Equal(["MediatR: the ledger marks 14.1.0 Unmeasured (R3b), not a measured floor"],
            PackedNuspec.DisagreementsWithLedger("Kronikol.Extensions.MediatR"));
}
