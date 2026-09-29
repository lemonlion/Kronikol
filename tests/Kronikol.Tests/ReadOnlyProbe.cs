namespace Kronikol.Tests;

/// <summary>
/// Whether a file marked read-only refuses this process a write. It does for every user on Windows, and for
/// every user but root on Linux and macOS: root writes a read-only file, because it is not held to a file's
/// mode. So a test that makes a write fail by marking a file read-only makes it fail only where this is true,
/// and a test that needs a refused write for every user refuses it another way.
/// </summary>
internal static class ReadOnlyProbe
{
    private static readonly Lazy<bool> Refuses = new(Probe);

    public static bool RefusesThisProcess => Refuses.Value;

    private static bool Probe()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kronikol-read-only-probe-{Guid.NewGuid():N}");
        File.WriteAllText(path, "");
        try
        {
            File.SetAttributes(path, FileAttributes.ReadOnly);
            try
            {
                using var _ = new FileStream(path, FileMode.Open, FileAccess.Write);
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
    }
}
