using Kronikol.Tests.Tool;
using YamlDotNet.RepresentationModel;

namespace Kronikol.Tests.Workflows;

/// <summary>
/// Release 5.27.14's run (2026-11-02) built, tested and packed, then stopped at the push: nuget.org answered 403 to a
/// key that had expired in a repository secret. These facts hold the release to NuGet's trusted publishing, where a
/// key lasts an hour and nothing is stored to expire, and to a manual run that publishes a tag as the tag's own run
/// would. A re-run cannot help a tag whose workflow has moved on, as a re-run reads the workflow at the tag.
/// </summary>
public class ReleaseWorkflowTests
{
    private static readonly YamlMappingNode Root = Load();
    private static readonly YamlMappingNode Job = (YamlMappingNode)((YamlMappingNode)Root["jobs"])["release"];
    private static readonly List<YamlMappingNode> Steps = ((YamlSequenceNode)Job["steps"]).Cast<YamlMappingNode>().ToList();

    [Fact]
    public void The_push_uses_the_key_the_login_step_gets_and_no_stored_secret()
    {
        var push = Named("Push to NuGet");
        var login = Named("NuGet login");

        Assert.Contains("--api-key ${{ steps.login.outputs.NUGET_API_KEY }}", Scalar(push, "run"));
        Assert.Equal("login", Scalar(login, "id"));
        Assert.StartsWith("NuGet/login@", Scalar(login, "uses"));
        Assert.DoesNotContain("secrets.", File.ReadAllText(WorkflowFile));
    }

    [Fact]
    public void The_job_may_ask_GitHub_for_the_token_the_login_swaps_for_a_key()
    {
        var permissions = (YamlMappingNode)Job["permissions"];

        Assert.Equal("write", Scalar(permissions, "id-token"));
        Assert.Equal("write", Scalar(permissions, "contents"));
    }

    [Fact]
    public void The_key_is_asked_for_straight_before_the_push_as_it_lasts_an_hour()
    {
        var login = Steps.FindIndex(step => Scalar(step, "name") == "NuGet login");
        var push = Steps.FindIndex(step => Scalar(step, "name") == "Push to NuGet");

        Assert.Equal(login + 1, push);
    }

    [Fact]
    public void A_tag_starts_a_run_and_so_can_a_person_naming_a_tag_that_exists()
    {
        var on = (YamlMappingNode)Root["on"];
        var inputs = (YamlMappingNode)((YamlMappingNode)on["workflow_dispatch"])["inputs"];

        Assert.Equal("v*", ((YamlSequenceNode)((YamlMappingNode)on["push"])["tags"]).Cast<YamlScalarNode>().Single().Value);
        Assert.Equal("true", Scalar((YamlMappingNode)inputs["tag"], "required"));
    }

    /// <summary>A manual run starts on main, so each of these reads the tag: else it builds main, packs version "main" and makes a release of that name.</summary>
    [Fact]
    public void A_manual_run_builds_versions_and_releases_the_tag_it_was_given()
    {
        Assert.Equal("${{ inputs.tag || github.ref_name }}", Scalar((YamlMappingNode)Job["env"], "RELEASE_TAG"));
        Assert.Equal("refs/tags/${{ env.RELEASE_TAG }}", Scalar(With(Steps.Single(step => (Scalar(step, "uses") ?? "").StartsWith("actions/checkout@"))), "ref"));
        Assert.Contains("${RELEASE_TAG#v}", Scalar(Named("Extract version from tag"), "run"));
        Assert.Equal("${{ env.RELEASE_TAG }}", Scalar(With(Named("Create GitHub Release")), "tag_name"));
        Assert.DoesNotContain("GITHUB_REF_NAME", File.ReadAllText(WorkflowFile));
    }

    private static string WorkflowFile => Path.Combine(BuiltTool.RepoRoot, ".github", "workflows", "release.yml");

    private static YamlMappingNode Named(string name) => Steps.Single(step => Scalar(step, "name") == name);

    private static YamlMappingNode With(YamlMappingNode step) => (YamlMappingNode)step["with"];

    private static string? Scalar(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? ((YamlScalarNode)value).Value : null;

    private static YamlMappingNode Load()
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(File.ReadAllText(WorkflowFile)));
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }
}
