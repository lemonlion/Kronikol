using Kronikol.ComponentDiagram;
using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Tests.Constants;

/// <summary>The <c>User</c> category: a person acting on the system — the caller of UI actions.</summary>
public class DependencyPaletteUserTests
{
    [Fact]
    public void User_resolves_to_its_own_type_colour_and_actor_shape()
    {
        Assert.Equal("User", DependencyCategories.User);
        Assert.Equal(DependencyType.User, DependencyPalette.Resolve(DependencyCategories.User));
        Assert.Equal(DependencyType.User, DependencyPalette.Resolve("user"));
        Assert.Equal("#7D3C98", DependencyPalette.GetColor(DependencyCategories.User));
        Assert.Equal("actor", DependencyPalette.GetSequenceShape(DependencyType.User));
    }

    [Fact]
    public void Component_diagram_draws_a_user_as_an_actor()
    {
        var log = new RequestResponseLog("Test", "t1", "Click", null, new Uri("http://localhost:4000/overview"), [],
            "web", "User", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false,
            CallerDependencyCategory: DependencyCategories.User)
        { IsUserAction = true };

        var relationships = ComponentDiagramGenerator.ExtractRelationships([log], null);
        var plain = ComponentDiagramGenerator.GeneratePlantUml(relationships, new ComponentDiagramOptions(), useC4: false);
        var c4 = ComponentDiagramGenerator.GeneratePlantUml(relationships, new ComponentDiagramOptions(), useC4: true);

        // A pure caller is already drawn as a person in both flavours; the User category keeps that true.
        Assert.Contains("**User**", plain);
        Assert.Contains("<<person>>", plain);
        Assert.Contains("Person(", c4);
    }
}
