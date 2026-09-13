using Kronikol.Reports;
using YamlDotNet.RepresentationModel;

namespace Kronikol.Tests.Reports;

/// <summary>
/// `Specifications.yml` is the default Specifications data format, and it has never been readable by a
/// YAML parser.
///
/// <para>Two separate defects, both long-standing. A feature's Gherkin description is usually several
/// lines and was written raw, so the second line landed at column 0 and ended the mapping — every one of
/// the six archived example files in this repository fails to load, at that line. And a step's sub-steps
/// were emitted as a deeper-indented <c>-</c> under the parent's plain scalar, which is not nesting: a
/// parser folds them into the parent's text, so <c>When the order is placed</c> with a sub-step came
/// back as <c>When the order is placed - And payment processed</c>.</para>
///
/// <para>The fold was silent while every step was a plain scalar. Once steps are quoted when they need
/// it — a step ending in <c>:</c>, which is what a Gherkin step introducing a data table looks like —
/// the same shape stops parsing altogether, so the nesting has to be real rather than visual. A step
/// with sub-steps is therefore written as a mapping; a step without them stays the plain string it has
/// always been, which is what keeps the file worth reading.</para>
/// </summary>
public class SpecificationsYamlStructureTests
{
    [Fact]
    public void A_feature_with_a_multi_line_description_still_parses()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Cake",
                Description = "As a dessert provider\nI want to create cakes from ingredients\nSo that customers can enjoy delicious cakes",
                Scenarios = [new Scenario { Id = "s1", DisplayName = "Bake one", Result = ExecutionResult.Passed }]
            }
        };

        var feature = Load(features, $"SpecsYaml_desc_{Guid.NewGuid():N}.yml");

        Assert.Equal(
            "As a dessert provider\nI want to create cakes from ingredients\nSo that customers can enjoy delicious cakes",
            Scalar(feature, "Description"));
    }

    /// <summary>
    /// The combination that turns the old silent fold into a hard parse failure: a step that has to be
    /// quoted, carrying sub-steps. A Gherkin step that introduces a data table ends with a colon, so
    /// this is an ordinary shape, not a contrived one.
    /// </summary>
    [Fact]
    public void A_quoted_step_with_sub_steps_parses_and_keeps_them_apart()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Muffins",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Bake a batch", Result = ExecutionResult.Passed,
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given",
                                Text = "a muffin recipe \"Classic\" with the following ingredients:",
                                SubSteps = [new ScenarioStep { Keyword = "And", Text = "payment processed" }]
                            },
                            new ScenarioStep { Keyword = "When", Text = "the muffins are prepared" }
                        ]
                    }
                ]
            }
        };

        var feature = Load(features, $"SpecsYaml_sub_{Guid.NewGuid():N}.yml");
        var steps = (YamlSequenceNode)((YamlMappingNode)((YamlSequenceNode)feature["Scenarios"])[0])["Steps"];

        Assert.Equal(2, steps.Children.Count);

        // The step that carries sub-steps is a mapping; the one that does not is still a plain string.
        var withSubSteps = (YamlMappingNode)steps[0];
        Assert.Equal("Given a muffin recipe \"Classic\" with the following ingredients:", Scalar(withSubSteps, "Step"));
        var subSteps = (YamlSequenceNode)withSubSteps["SubSteps"];
        Assert.Equal("And payment processed", ((YamlScalarNode)subSteps[0]).Value);

        Assert.Equal("When the muffins are prepared", ((YamlScalarNode)steps[1]).Value);
    }

    [Fact]
    public void A_step_without_sub_steps_is_still_a_plain_string()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Place one", Result = ExecutionResult.Passed,
                        Steps = [new ScenarioStep { Keyword = "When", Text = "the order is placed" }]
                    }
                ]
            }
        };

        var feature = Load(features, $"SpecsYaml_flat_{Guid.NewGuid():N}.yml");
        var steps = (YamlSequenceNode)((YamlMappingNode)((YamlSequenceNode)feature["Scenarios"])[0])["Steps"];

        Assert.IsType<YamlScalarNode>(steps[0]);
        Assert.Equal("When the order is placed", ((YamlScalarNode)steps[0]).Value);
    }

    /// <summary>The public entry point shares the writer, so it has to share the fix.</summary>
    [Fact]
    public void The_public_GenerateYamlSpecs_writes_the_same_shape()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Cake",
                Description = "Line one\nLine two",
                Scenarios = [new Scenario { Id = "s1", DisplayName = "Bake", Result = ExecutionResult.Passed }]
            }
        };

        var path = ReportGenerator.GenerateYamlSpecs([], features, $"SpecsYaml_public_{Guid.NewGuid():N}.yml", "Specs");

        Assert.Equal("Line one\nLine two", Scalar(FirstFeature(path), "Description"));
    }

    private static YamlMappingNode Load(Feature[] features, string fileName) =>
        FirstFeature(ReportGenerator.GenerateSpecificationsData(features, fileName, "Specs", DataFormat.Yaml));

    private static YamlMappingNode FirstFeature(string path)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(File.ReadAllText(path)));
        var root = (YamlMappingNode)stream.Documents[0].RootNode;
        return (YamlMappingNode)((YamlSequenceNode)root["Features"])[0];
    }

    private static string? Scalar(YamlMappingNode node, string key) => ((YamlScalarNode)node[key]).Value;
}
