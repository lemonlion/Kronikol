using Kronikol.Tests.Packaging;

namespace Kronikol.Tests.BigQuery;

/// <summary>
/// The client version the packed extension asks for. A floating reference (<c>3.*</c>) is packed as the newest version
/// of the day, so from 3.0.28 every release asked for 3.12.0, a consumer that pinned 3.11.0 failed restore (NU1109,
/// NU1605), and one that moved to 3.12.0 ran its services on a REST call its emulator answered differently (#205,
/// <c>plans/CLIENT_FLOORS_PLAN.md</c> R1). The extension pins the oldest 3.x its suite passes on (3.0.0): no advisory
/// in that graph, and the client's assemblies are strong-named, so a consumer on a later 3.x compiles against it. This
/// reads the floor back from the nuspec <c>dotnet pack</c> writes and holds it to <see cref="FloorLedger"/>. Moving the
/// floor is a decision: run the suite on the new one first (<c>-p:FloorRun=true</c>).
/// </summary>
public class BigQueryPackageTests
{
    [Fact]
    public void The_packed_extension_asks_for_its_measured_client_floor() =>
        Assert.Empty(PackedNuspec.DisagreementsWithLedger("Kronikol.Extensions.BigQuery"));
}
