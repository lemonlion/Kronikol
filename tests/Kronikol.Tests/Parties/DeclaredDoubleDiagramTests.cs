using Kronikol.ComponentDiagram;
using Kronikol.Parties;
using Kronikol.PlantUml;

namespace Kronikol.Tests.Parties;

/// <summary>
/// #190 R1, Q11: a party the run declares a test double carries «test double» on its participant, in the sequence
/// diagrams as a stereotype after the alias and before any colour, and in the component diagram as a line of its label
/// (rectangles keep their own styling stereotypes). Nothing else is drawn differently, and nothing is drawn until a
/// double is declared.
/// </summary>
public class DeclaredDoubleDiagramTests
{
    private static readonly HashSet<string> Cow = new(StringComparer.Ordinal) { "Cow Service" };

    private static string[] SequenceLines(bool colours = false) =>
        PlantUmlCreator.GetPlantUmlImageTagsPerTestId(PartiesBlockTests.Logs(), sequenceDiagramParticipantColors: colours)
            .Single().PlantUmls.Single().PlainText.Split('\n');

    private static string Declaration(string[] lines, string name) => lines.Single(l => l.Contains($"\"{name}\" as ", StringComparison.Ordinal));

    [Fact]
    public void A_declared_double_carries_the_stereotype_after_its_alias_in_the_sequence_diagram()
    {
        using (DiagramParties.Declare(Cow))
        {
            var lines = SequenceLines();

            Assert.EndsWith(" <<test double>>", Declaration(lines, "Cow Service").TrimEnd('\r'));
            Assert.DoesNotContain("<<test double>>", Declaration(lines, "Breakfast Provider"));
            Assert.DoesNotContain("<<test double>>", Declaration(lines, "Caller"));
        }
    }

    // The page reads a declaration's colour after its alias (plantuml-browser-render-script.js's DECLARED), and keeps it
    // past the stereotype; the E2E DeclaredDoubleTests holds the drawing to it.
    [Fact]
    public void The_stereotype_comes_before_the_participants_colour()
    {
        var pairId = Guid.NewGuid();
        Kronikol.Tracking.RequestResponseLog Log(Kronikol.Tracking.RequestResponseType type) =>
            new("t1", "t1", "SELECT", null, new Uri("http://cows/milk"), [], "Cow Service", "Breakfast Provider", type,
                Guid.NewGuid(), pairId, TrackingIgnore: false, DependencyCategory: "SqlServer");

        using (DiagramParties.Declare(Cow))
        {
            var lines = PlantUmlCreator.GetPlantUmlImageTagsPerTestId([Log(Kronikol.Tracking.RequestResponseType.Request), Log(Kronikol.Tracking.RequestResponseType.Response)],
                sequenceDiagramParticipantColors: true).Single().PlantUmls.Single().PlainText.Split('\n');
            Assert.Equal("database \"Cow Service\" as cowService <<test double>> #0E7C86", Declaration(lines, "Cow Service").TrimEnd('\r'));
        }
    }

    [Fact]
    public void Nothing_is_drawn_differently_until_a_double_is_declared()
    {
        var undeclared = SequenceLines();
        using (DiagramParties.Declare(null))
            Assert.Equal(undeclared, SequenceLines());
        Assert.DoesNotContain(undeclared, l => l.Contains("test double", StringComparison.Ordinal));
        using (DiagramParties.Declare(new HashSet<string>(StringComparer.Ordinal) { "cow service" }))
            Assert.Equal(undeclared, SequenceLines());
    }

    [Fact]
    public void The_scope_ends_with_its_disposal_and_restores_an_outer_one()
    {
        using (DiagramParties.Declare(Cow))
        {
            using (DiagramParties.Declare(new HashSet<string>(StringComparer.Ordinal) { "Breakfast Provider" }))
                Assert.False(DiagramParties.IsDeclaredDouble("Cow Service"));
            Assert.True(DiagramParties.IsDeclaredDouble("Cow Service"));
        }
        Assert.False(DiagramParties.IsDeclaredDouble("Cow Service"));
    }

    public static TheoryData<bool> C4() => [true, false];

    [Theory]
    [MemberData(nameof(C4))]
    public void A_declared_double_says_so_in_its_component_diagram_label(bool useC4)
    {
        ComponentRelationship[] relationships =
        [
            new("Caller", "Breakfast Provider", "HTTP", ["GET"], 1, 1),
            new("Breakfast Provider", "Cow Service", "HTTP", ["GET"], 1, 1)
        ];
        var plain = ComponentDiagramGenerator.GeneratePlantUml(relationships, useC4: useC4);

        string declared;
        using (DiagramParties.Declare(Cow))
            declared = ComponentDiagramGenerator.GeneratePlantUml(relationships, useC4: useC4);

        var cow = declared.Split('\n').Single(l => l.Contains("Cow Service", StringComparison.Ordinal) && !l.Contains("Rel", StringComparison.Ordinal) && !l.Contains("-->", StringComparison.Ordinal));
        Assert.Contains("«test double»", cow);
        Assert.DoesNotContain("test double", plain);
        Assert.Equal(1, declared.Split("test double").Length - 1);
    }
}
