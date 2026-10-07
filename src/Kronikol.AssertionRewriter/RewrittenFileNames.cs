using System.IO;
using System.Linq;

namespace Kronikol.AssertionRewriter;

/// <summary>Names for the rewritten copies of source files: kept apart from the task so a test can call it without MSBuild.</summary>
internal static class RewrittenFileNames
{
    /// <summary>
    /// The name a rewritten copy of <paramref name="filePath"/> takes in the intermediate directory: its own name after a
    /// hash of its path, so two files of one name in different folders do not collide. The hash is the same in every
    /// build. Until 4.7.3 it was <c>string.GetHashCode</c>, which .NET randomises per process, so every
    /// <c>dotnet build</c> wrote each rewritten file under a new name and left the last build's beside it.
    /// </summary>
    public static string Of(string filePath)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(filePath));
        return string.Concat(hash.Take(4).Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)))
               + "_" + Path.GetFileName(filePath);
    }
}
