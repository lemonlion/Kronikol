using System.Text.RegularExpressions;

namespace Kronikol.Tests.Templates;

/// <summary>
/// Every workflow here, every composite action under <c>templates/github-actions/</c> and the workflows their
/// READMEs give a consumer use one major of each action. The live lanes of 2.2 and 2.3 arrived on
/// <c>checkout@v7</c> and <c>setup-dotnet@v6</c> while CI, CodeQL, CI Summary Preview and the release stayed
/// on v5, so what the lanes proved on GitHub was not what the repository's own runs used.
///
/// <para>A commit pin counts as the version its comment names (<c>@&lt;sha&gt; # v7.0.1</c>), which the history
/// action's facts require of every pin it makes. Which major is newest is not something a test can know:
/// that is read from each action's releases when the versions move.</para>
/// </summary>
public class WorkflowActionVersionTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly Regex Uses = new(
        @"uses:\s*(?<action>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)(?:/[^@\s]*)?@(?<ref>[^\s#`]+)(?:[ \t]*#[ \t]*(?<comment>\S+))?");

    [Fact]
    public void Each_action_is_used_at_one_major_across_the_workflows_the_composite_actions_and_their_readmes()
    {
        var seen = new Dictionary<string, Dictionary<string, SortedSet<string>>>(StringComparer.Ordinal);
        foreach (var (file, text) in Sources())
        {
            foreach (Match use in Uses.Matches(text))
            {
                var action = use.Groups["action"].Value;
                if (action.Equals("lemonlion/Kronikol", StringComparison.OrdinalIgnoreCase))
                    continue;

                var major = Major(use.Groups["ref"].Value, use.Groups["comment"].Value);
                Assert.True(major is not null, $"{file}: {use.Value} pins no version this fact can read (a tag, or a commit with its version in a comment)");
                var versions = seen.TryGetValue(action, out var found) ? found : seen[action] = new(StringComparer.Ordinal);
                (versions.TryGetValue(major!, out var files) ? files : versions[major!] = new(StringComparer.Ordinal)).Add(file);
            }
        }

        Assert.Contains("actions/checkout", seen.Keys);
        var mixed = seen
            .Where(action => action.Value.Count > 1)
            .Select(action => $"{action.Key}: " + string.Join("; ", action.Value.Select(v => $"{v.Key} in {string.Join(", ", v.Value)}")))
            .ToList();
        Assert.True(mixed.Count == 0, "actions used at more than one major:\n" + string.Join("\n", mixed));
    }

    /// <summary>The major a reference names: <c>v7</c> for <c>@v7</c> or <c>@v7.0.1</c>, and for a commit the major of its comment.</summary>
    private static string? Major(string reference, string comment)
    {
        var tag = Regex.Match(reference, @"^(v\d+)(\.\d+)*$");
        if (tag.Success)
            return tag.Groups[1].Value;

        var pinned = Regex.Match(comment, @"^(v\d+)(\.\d+)*$");
        return Regex.IsMatch(reference, "^[0-9a-f]{40}$") && pinned.Success ? pinned.Groups[1].Value : null;
    }

    private static IEnumerable<(string File, string Text)> Sources()
    {
        var workflows = Directory.EnumerateFiles(Path.Combine(RepoRoot, ".github", "workflows"), "*.y*ml");
        var actions = Directory.EnumerateFiles(Path.Combine(RepoRoot, "templates", "github-actions"), "action.y*ml", SearchOption.AllDirectories);
        foreach (var path in workflows.Concat(actions))
            yield return (Relative(path), File.ReadAllText(path));

        foreach (var readme in Directory.EnumerateFiles(Path.Combine(RepoRoot, "templates", "github-actions"), "README.md", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(readme).ReplaceLineEndings("\n");
            var fences = Regex.Matches(text, @"^```ya?ml\n(.*?)^```", RegexOptions.Singleline | RegexOptions.Multiline);
            yield return (Relative(readme), string.Concat(fences.Select(f => f.Groups[1].Value)));
        }
    }

    private static string Relative(string path) => Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');
}
