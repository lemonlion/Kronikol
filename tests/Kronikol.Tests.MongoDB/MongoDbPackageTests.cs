using Kronikol.Tests.Packaging;
using MongoDB.Driver;

namespace Kronikol.Tests.MongoDB;

/// <summary>
/// The driver version a packed extension asks for. A floating reference (<c>3.*</c>) is packed as the newest version of
/// the day, so each release raised the floor every consumer had to meet, and a consumer that pins an older driver met
/// NuGet's downgrade error (#168 F21, <c>plans/MONGODB_DETAILED_PAYLOADS_PLAN.md</c> Q18). The 3.x extension pins the
/// oldest driver its suite passes on whose dependencies carry no known advisory (3.9.0); the V2 extension pins the first
/// 2.x with strong-named assemblies (2.28.0), and every 2.x driver brings Snappier 1.0.0 and SharpCompress 0.30.1, which
/// carry advisories. This reads the floor back from the nuspec <c>dotnet pack</c> writes and holds it to
/// <see cref="FloorLedger"/>, which gives each floor's reason. Moving a floor is a decision: run the suite on the new one
/// first.
/// </summary>
public class MongoDbPackageTests
{
    [Fact]
    public void The_packed_extension_asks_for_its_measured_driver_floor()
    {
        // This file runs in both suites, each on its own driver line, so the driver it runs on names the extension.
        var major = typeof(MongoClient).Assembly.GetName().Version!.Major;
        var project = major switch
        {
            3 => "Kronikol.Extensions.MongoDB",
            2 => "Kronikol.Extensions.MongoDB.V2",
            _ => throw new InvalidOperationException($"No extension is built on driver {major}.x.")
        };

        Assert.Empty(PackedNuspec.DisagreementsWithLedger(project));
    }
}
