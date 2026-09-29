using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Kronikol.Tests.Templates;

/// <summary>
/// A composite action's <c>action.yml</c>, read as the runner reads it: the inputs with their defaults, the outputs,
/// and the steps, each a <c>run:</c> step with its <c>shell</c> or a <c>uses:</c> step with its <c>with</c>.
/// Extracted from <c>PrReportLinkActionTests</c> for roadmap 2.3 (plans/HISTORY_ACTION_PLAN.md §5.2), which has four
/// actions of several steps; the one-step members 2.2's facts use (<see cref="StepEnv"/>, <see cref="Script"/>,
/// <see cref="EnvironmentFor"/>) still read an action of one step.
/// </summary>
internal sealed record ActionDefinition(
    string Directory,
    IReadOnlyDictionary<string, (bool Required, string Default)> Inputs,
    IReadOnlyDictionary<string, string> Outputs,
    IReadOnlyList<ActionStep> Steps)
{
    private static readonly Regex InputExpression = new(@"^\$\{\{\s*inputs\.([A-Za-z0-9_-]+)\s*\}\}$");

    public static ActionDefinition Load(string directory)
    {
        var root = LoadYaml(File.ReadAllText(Path.Combine(directory, "action.yml")));

        var inputs = root.Children.TryGetValue(new YamlScalarNode("inputs"), out var inputsNode)
            ? ((YamlMappingNode)inputsNode).Children.ToDictionary(
                kv => ((YamlScalarNode)kv.Key).Value!,
                kv =>
                {
                    var input = (YamlMappingNode)kv.Value;
                    return (Required: Scalar(input, "required") == "true", Default: Scalar(input, "default") ?? "");
                },
                StringComparer.Ordinal)
            : new Dictionary<string, (bool, string)>(StringComparer.Ordinal);

        var outputs = root.Children.TryGetValue(new YamlScalarNode("outputs"), out var outputsNode)
            ? ((YamlMappingNode)outputsNode).Children.ToDictionary(
                kv => ((YamlScalarNode)kv.Key).Value!,
                kv => Scalar((YamlMappingNode)kv.Value, "value") ?? "",
                StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        var runs = (YamlMappingNode)root["runs"];
        Assert.Equal("composite", Scalar(runs, "using"));
        var steps = ((YamlSequenceNode)runs["steps"]).Children.Cast<YamlMappingNode>().Select(ActionStep.From).ToList();

        return new ActionDefinition(directory, inputs, outputs, steps);
    }

    public static YamlMappingNode LoadYaml(string text)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    public static string? Scalar(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? ((YamlScalarNode)value).Value : null;

    /// <summary>The environment of an action's only step.</summary>
    public IReadOnlyDictionary<string, string> StepEnv => Assert.Single(Steps).Env;

    /// <summary>The <c>script</c> an action's only step passes to <c>actions/github-script</c>.</summary>
    public string Script => Assert.Single(Steps).With["script"];

    /// <summary>What the runner would put in an action's only step's environment for these inputs.</summary>
    public Dictionary<string, string> EnvironmentFor(IReadOnlyDictionary<string, string> given)
    {
        foreach (var (name, (required, _)) in Inputs)
            Assert.True(!required || given.ContainsKey(name), $"the runner refuses a call without the required input {name}");

        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (variable, expression) in StepEnv)
        {
            var match = InputExpression.Match(expression);
            Assert.True(match.Success, $"env {variable} is `{expression}`, which is not an input this test can supply");

            var input = match.Groups[1].Value;
            Assert.True(Inputs.ContainsKey(input), $"env {variable} reads inputs.{input}, which action.yml does not declare");
            environment[variable] = given.TryGetValue(input, out var value) ? value : Inputs[input].Default;
        }

        return environment;
    }
}

/// <summary>One step of a composite action.</summary>
internal sealed record ActionStep(
    string? Id,
    string? Name,
    string? If,
    string? Run,
    string? Shell,
    string? Uses,
    IReadOnlyDictionary<string, string> With,
    IReadOnlyDictionary<string, string> Env,
    bool ContinueOnError)
{
    public static ActionStep From(YamlMappingNode node)
    {
        IReadOnlyDictionary<string, string> Map(string key) =>
            node.Children.TryGetValue(new YamlScalarNode(key), out var value)
                ? ((YamlMappingNode)value).Children.ToDictionary(kv => ((YamlScalarNode)kv.Key).Value!, kv => ((YamlScalarNode)kv.Value).Value ?? "", StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);

        return new ActionStep(
            ActionDefinition.Scalar(node, "id"),
            ActionDefinition.Scalar(node, "name"),
            ActionDefinition.Scalar(node, "if"),
            ActionDefinition.Scalar(node, "run"),
            ActionDefinition.Scalar(node, "shell"),
            ActionDefinition.Scalar(node, "uses"),
            Map("with"),
            Map("env"),
            ActionDefinition.Scalar(node, "continue-on-error") == "true");
    }
}
