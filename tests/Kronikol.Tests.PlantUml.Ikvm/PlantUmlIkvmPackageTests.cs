using Kronikol.Tests.Packaging;
using Xunit;

namespace Kronikol.Tests.PlantUml.Ikvm;

/// <summary>
/// What the packed <c>Kronikol.PlantUml.Ikvm</c> asks its consumers for: a measured floor for each dependency, not the
/// newest release of the day it was packed (<c>plans/CLIENT_FLOORS_PLAN.md</c> R3c). This packs the project and holds
/// its nuspec to <see cref="FloorLedger"/>, which gives each floor's evidence. Moving a floor is a decision: run the
/// suite on the new one first (<c>-p:FloorRun=true</c>), then move the csproj and the ledger together.
/// </summary>
public class PlantUmlIkvmPackageTests
{
    [Fact]
    public void The_packed_project_asks_for_its_measured_floors() =>
        Assert.Empty(PackedNuspec.DisagreementsWithLedger("Kronikol.PlantUml.Ikvm"));

    /// <summary>
    /// The renderer is compiled against the assembly IKVM compiles from PlantUML's jar, and a project that takes the
    /// package has no other way to get it: until 5.27.13 the package carried the renderer alone, and a project that took
    /// it from nuget.org failed at its first render, unable to load <c>net.sourceforge.plantuml</c>
    /// (<c>plans/CLIENT_FLOORS_PLAN.md</c> F40).
    /// </summary>
    [Fact]
    public void The_package_carries_the_PlantUML_assembly_the_renderer_is_compiled_against()
    {
        var plantUml = typeof(IkvmPlantUmlRenderer).Assembly.GetReferencedAssemblies()
            .Single(reference => reference.Name!.StartsWith("net.sourceforge.plantuml", StringComparison.Ordinal));

        Assert.Contains($"lib/net10.0/{plantUml.Name}.dll", PackedNuspec.Files("Kronikol.PlantUml.Ikvm"));
    }
}
