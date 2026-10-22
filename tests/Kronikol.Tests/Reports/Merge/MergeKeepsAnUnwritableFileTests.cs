using System.Security.AccessControl;
using System.Security.Principal;
using Kronikol.Tool;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// <c>kronikol merge</c> leaves a data file this process may not write as it is, and says it could not write it, as the
/// run's own writer has since 4.32.1. A merged <c>.krkl</c> is written to a temporary file and renamed over its name, and a
/// rename needs only the folder, so it replaced a read-only file on Linux and macOS, and on Windows, which refuses a rename
/// over a read-only file, one whose ACL refuses this user a write (5.2.7). A merged <c>.json</c> was refused all along.
/// </summary>
public class MergeKeepsAnUnwritableFileTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("kronikol-merge-unwritable").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    private static string Mergeable => Path.Combine(AppContext.BaseDirectory, "TestData", "Reports", "failing-with-steps.mergeable.json");

    [Theory]
    // -o names the data file when it ends .krkl, and otherwise the report, whose data file beside it is a .json
    [InlineData("Merged.krkl", "Merged.krkl")]
    [InlineData("Merged.html", "Merged.json")]
    public void A_data_file_this_process_may_not_write_is_left_as_it_is(string o, string name)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() || ReadOnlyProbe.RefusesThisProcess,
            "a read-only file does not refuse this process (root), so the write this fact needs cannot fail");
        var target = Path.Combine(_root, name);
        File.WriteAllText(target, "an earlier merge");
        RefuseWrites(target);
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = MergeCommand.Run([Mergeable, "-o", Path.Combine(_root, o)], output, error, _ => null);

        Assert.NotEqual(0, exit);
        Assert.Contains("Could not write -o", error.ToString(), StringComparison.Ordinal);
        Assert.Contains(name, error.ToString(), StringComparison.Ordinal);
        Assert.Equal("an earlier merge", File.ReadAllText(target));
        Assert.DoesNotContain(Directory.GetFiles(_root), file => file.EndsWith(".tmp", StringComparison.Ordinal));
    }

    /// <summary>
    /// Makes <paramref name="path"/> a file this process may not write and may still rename over, which is what a read-only
    /// file is on Linux and macOS. Windows refuses a rename over a read-only file, so there the file's ACL refuses this user
    /// a write and not a delete.
    /// </summary>
    private static void RefuseWrites(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetAttributes(path, FileAttributes.ReadOnly);
            return;
        }

        var file = new FileInfo(path);
        var acl = file.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
            FileSystemRights.WriteData | FileSystemRights.AppendData, AccessControlType.Deny));
        file.SetAccessControl(acl);
    }
}
