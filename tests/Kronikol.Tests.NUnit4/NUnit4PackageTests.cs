using Kronikol.Tests.Packaging;
using NUnit.Framework;

namespace Kronikol.Tests.NUnit4;

/// <summary>
/// What the packed <c>Kronikol.NUnit4</c> asks its consumers for: a measured floor for each dependency, not the newest
/// release of the day it was packed (<c>plans/CLIENT_FLOORS_PLAN.md</c> R3a). This packs the project and holds its
/// nuspec to <see cref="FloorLedger"/>, which gives each floor's evidence. Moving a floor is a decision: run the suite
/// on the new one first (<c>-p:FloorRun=true</c>), then move the csproj and the ledger together.
/// </summary>
public class NUnit4PackageTests
{
    [Test]
    public void The_packed_project_asks_for_its_measured_floors() =>
        Assert.That(PackedNuspec.DisagreementsWithLedger("Kronikol.NUnit4"), Is.Empty);
}
