using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Kronikol.Tests.Packaging;

/// <summary>
/// A package README is shown on nuget.org, which keeps a link only when its address is absolute http(s) or a
/// <c>#fragment</c>. Any other link keeps its text and loses its address (NuGetGallery's
/// <c>MarkdownService</c>: "Allow only http or https links in markdown"), and a relative image is not shown at
/// all. On GitHub the same link works, so nothing but a check like this one sees it.
///
/// <para>The READMEs are found the way <c>dotnet pack</c> finds them: every <c>PackageReadmeFile</c> in
/// <c>Directory.Build.props</c> or a project under <c>src/</c> or <c>templates/</c>, and the packed file of that
/// name. A package that gains a README of its own is checked without this file changing.</para>
/// </summary>
public class PackageReadmeLinkTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    public static TheoryData<string> PackageReadmes()
    {
        var declaring = new[] { Path.Combine(RepoRoot, "Directory.Build.props") }
            .Concat(Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(RepoRoot, "templates"), "*.csproj", SearchOption.AllDirectories));

        var readmes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in declaring.Where(File.Exists))
        {
            var document = XDocument.Load(file);
            var name = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "PackageReadmeFile")?.Value.Trim();
            if (string.IsNullOrEmpty(name))
                continue;

            // The packed item whose file name is the README's; a project that names one it does not pack
            // yields the bare name, which the fact then reports as missing.
            var source = document.Descendants()
                .Where(e => e.Name.LocalName == "None"
                            && string.Equals(e.Attribute("Pack")?.Value, "true", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Attribute("Include")?.Value.Replace("$(MSBuildThisFileDirectory)", "", StringComparison.Ordinal))
                .FirstOrDefault(include => include is not null && Path.GetFileName(include) == name) ?? name;

            var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, source));
            readmes.Add(Path.GetRelativePath(RepoRoot, path).Replace('\\', '/'));
        }

        var data = new TheoryData<string>();
        foreach (var readme in readmes)
            data.Add(readme);
        return data;
    }

    [Theory]
    [MemberData(nameof(PackageReadmes))]
    public void A_package_readme_links_only_to_absolute_addresses(string readme)
    {
        var path = Path.Combine(RepoRoot, readme.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"{readme} is named as a package README and does not exist");

        // What sits in a fence is code, not a link.
        var prose = Regex.Replace(File.ReadAllText(path), @"^(```|~~~).*?^\1", "", RegexOptions.Singleline | RegexOptions.Multiline);

        var addresses = Regex.Matches(prose, @"\]\(\s*<?([^)\s>]+)").Select(m => m.Groups[1].Value)
            .Concat(Regex.Matches(prose, @"^\s*\[[^\]]+\]:\s*<?([^\s>]+)", RegexOptions.Multiline).Select(m => m.Groups[1].Value))
            .Concat(Regex.Matches(prose, @"\b(?:href|src)\s*=\s*[""']([^""']+)", RegexOptions.IgnoreCase).Select(m => m.Groups[1].Value))
            .ToList();
        Assert.NotEmpty(addresses);

        var emptied = addresses.Where(a => !Regex.IsMatch(a, "^(https?://|#)", RegexOptions.IgnoreCase)).ToList();
        Assert.True(emptied.Count == 0,
            $"{readme} is shown on nuget.org, which renders these addresses empty: {string.Join(", ", emptied)}. "
            + "Link to an absolute https:// address instead.");
    }
}
