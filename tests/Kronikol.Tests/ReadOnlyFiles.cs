namespace Kronikol.Tests;

/// <summary>
/// Whether marking a file read-only makes a write to it fail on this machine (tests that simulate an
/// unwritable file that way skip when it does not). Windows enforces the mark, and so do Linux and macOS for
/// an ordinary user, but root ignores the write bits the mark clears; <see cref="ReadOnlyFilesTests"/> says why
/// that matters and holds every such test to the skip.
/// </summary>
internal static class ReadOnlyFiles
{
    public const string NotEnforcedReason =
        "a file marked read-only is still writable here: the tests run as root, which ignores permission bits";

    private static readonly Lazy<bool> Enforced = new(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"kronikol-read-only-probe-{Guid.NewGuid():N}");
        File.WriteAllText(path, "");
        try
        {
            File.SetAttributes(path, FileAttributes.ReadOnly);
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.Write)) { }
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    });

    public static bool AreEnforced => Enforced.Value;
}
