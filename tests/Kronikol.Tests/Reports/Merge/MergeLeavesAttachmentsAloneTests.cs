using Kronikol.Reports;
using Kronikol.Tool;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// <c>kronikol merge &lt;folder&gt;</c> sweeps for reports, and a run keeps what it attached in <c>attachments/</c> beside its
/// report, so a shard with an <c>attachments/openapi.json</c> stopped the merge with exit 1: the recipe the wiki gives could
/// not read a folder a run had written (plans/CALLS_AND_EVENTS_PLAN.md F24).
/// </summary>
[Collection("DiagramsFetcher")]
public class MergeLeavesAttachmentsAloneTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-merge-attachments").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    private string WriteShard(string folder)
    {
        var shard = Path.Combine(_dir, folder);
        Directory.CreateDirectory(shard);
        File.WriteAllText(Path.Combine(shard, "TestRunReport.json"), """
            {
              "mergeableFormatVersion": 1,
              "kronikolVersion": "5.27.13",
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:01:00Z",
              "features": [ { "name": "Orders", "scenarios": [ { "id": "s1", "name": "Place order", "result": "Passed", "durationSeconds": 1.0 } ] } ]
            }
            """);
        return shard;
    }

    private (int Exit, string Output, string Error) Merge(params string[] inputs)
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        var exit = MergeCommand.Run([.. inputs, "-o", Path.Combine(_dir, "merged", "Combined.html")], output, error, _ => null);
        return (exit, output.ToString(), error.ToString());
    }

    [Theory]
    [InlineData("attachments/openapi.json")]
    [InlineData("attachments/nested/contract.json")]
    [InlineData("Attachments/openapi.json")]
    public void A_folder_holding_an_attached_json_file_merges(string attachment)
    {
        var shard = WriteShard("shard");
        var path = Path.Combine(shard, attachment.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "openapi": "3.1.0" }""");

        var (exit, output, error) = Merge(shard);

        Assert.True(exit == 0, error);
        Assert.Contains("Merging 1 report file(s):", output, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetFileName(path), output, StringComparison.Ordinal);
    }

    /// <summary>What a sweep finds is left alone, and what it is given is read: a file named on the command line is still refused for what it is.</summary>
    /// <summary>A shard someone put under such a folder is left out too, so the merge says it left something: it would otherwise lose a shard without a word.</summary>
    [Fact]
    public void A_merge_says_how_many_files_it_left_under_attachments()
    {
        var shard = WriteShard("artifacts/shard-1");
        var second = WriteShard("artifacts/attachments");
        Assert.NotEqual(shard, second);

        var (exit, _, error) = Merge(Path.Combine(_dir, "artifacts"));

        Assert.Equal(0, exit);
        Assert.Contains("left 1 file under attachments/", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_merge_of_nothing_but_attachments_says_why_it_found_no_shard()
    {
        WriteShard("artifacts/attachments");

        var (_, _, error) = Merge(Path.Combine(_dir, "artifacts"));

        Assert.Contains("left 1 file under attachments/", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_merge_with_nothing_under_attachments_says_nothing_of_it()
    {
        var (exit, _, error) = Merge(WriteShard("shard"));

        Assert.Equal(0, exit);
        Assert.DoesNotContain("attachments", error, StringComparison.Ordinal);
    }

    /// <summary>What a merge leaves in the run's own folders is routine, so it is not called an attachment.</summary>
    [Theory]
    [InlineData("baseline")]
    [InlineData("runs/gh_1_1")]
    public void A_merge_says_nothing_of_what_it_left_in_the_runs_own_folders(string reserved)
    {
        var shard = WriteShard("shard");
        var folder = Path.Combine(shard, reserved.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "TestRunReport.json"), "{}");

        var (exit, _, error) = Merge(shard);

        Assert.Equal(0, exit);
        Assert.DoesNotContain("attachments", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_attached_file_named_on_the_command_line_is_still_read()
    {
        var shard = WriteShard("shard");
        var path = Path.Combine(shard, "attachments", "openapi.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "openapi": "3.1.0" }""");

        var (exit, _, error) = Merge(path);

        Assert.NotEqual(0, exit);
        Assert.Contains("openapi.json", error, StringComparison.Ordinal);
    }

    /// <summary>A shard kept in a folder someone named <c>attachments</c> is found when that folder is what the merge was given.</summary>
    [Fact]
    public void A_folder_named_attachments_that_is_the_root_of_the_sweep_is_read()
    {
        var shard = WriteShard("attachments");

        var (exit, output, error) = Merge(shard);

        Assert.True(exit == 0, error);
        Assert.Contains("Merging 1 report file(s):", output, StringComparison.Ordinal);
    }

    /// <summary><c>kronikol query</c> sweeps the same way, through the same rule.</summary>
    [Fact]
    public void Reserved_holds_for_attachments_as_for_the_other_folders()
    {
        Assert.True(ReportFolders.IsReserved(_dir, Path.Combine(_dir, "attachments", "TestRunReport.json")));
        Assert.False(ReportFolders.IsReserved(Path.Combine(_dir, "attachments"), Path.Combine(_dir, "attachments", "TestRunReport.json")));
        Assert.False(ReportFolders.IsReserved(_dir, Path.Combine(_dir, "attachments.json")));
    }
}
