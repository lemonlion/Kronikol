using Kronikol.Parties;
using Kronikol.Reports;

namespace Kronikol.Tests.Parties;

/// <summary>
/// #190 R1: the parties file a repository's suites share, found the way the history ledger is, and read so that a
/// broken one is a diagnostic and never a failed run (plans/PARTY_IDENTITY_PLAN.md §5.2).
/// </summary>
public class PartiesFileDiscoveryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("kronikol-parties").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Repository()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        var output = Path.Combine(_root, "tests", "Api.Tests", "bin", "Debug", "net10.0");
        Directory.CreateDirectory(output);
        return output;
    }

    private string WriteShared(string text)
    {
        var path = Path.Combine(_root, ".kronikol", "parties.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private static Func<string, string?> Env(string? parties = null) => name => name == PartiesFile.EnvironmentVariable ? parties : null;

    [Fact]
    public void Is_found_at_the_repository_above_the_test_output()
    {
        var output = Repository();
        var shared = WriteShared("""{ "partiesFormatVersion": 1, "parties": [] }""");

        Assert.Equal(shared, PartiesFile.Locate(null, Path.Combine(output, "Reports"), Env(), output));
    }

    [Fact]
    public void Is_not_looked_for_anywhere_else_and_its_absence_is_no_file()
    {
        var output = Repository();

        Assert.Null(PartiesFile.Locate(null, Path.Combine(output, "Reports"), Env(), output));
    }

    [Fact]
    public void The_variable_wins_over_the_repository_and_the_option_over_both()
    {
        var output = Repository();
        WriteShared("""{ "parties": [] }""");
        var fromEnv = Path.Combine(_root, "env-parties.json");
        var fromOption = Path.Combine(_root, "option-parties.json");

        Assert.Equal(fromEnv, PartiesFile.Locate(null, output, Env(fromEnv), output));
        Assert.Equal(fromOption, PartiesFile.Locate(fromOption, output, Env(fromEnv), output));
    }

    [Fact]
    public void A_relative_path_is_taken_against_the_repository()
    {
        var output = Repository();

        Assert.Equal(Path.Combine(_root, "config", "parties.json"), PartiesFile.Locate("config/parties.json", output, Env(), output));
    }

    [Fact]
    public void Reads_the_rows_with_kinds_ignoring_case()
    {
        var (rows, findings) = PartiesFile.Parse("""
            { "partiesFormatVersion": 1,
              "parties": [ { "name": "Cow Service", "id": "cow-service", "kind": "Double", "note": "Dependencies.Fakes.CowService" },
                           { "name": "CosmosDB", "id": "orders-store" },
                           { "name": "Breakfast Provider", "kind": "UNDERTEST" } ] }
            """, "parties.json");

        Assert.Empty(findings);
        Assert.Equal([
            new PartyDescriptor("Cow Service") { Id = "cow-service", Kind = PartyKind.Double, Note = "Dependencies.Fakes.CowService" },
            new PartyDescriptor("CosmosDB") { Id = "orders-store" },
            new PartyDescriptor("Breakfast Provider") { Kind = PartyKind.UnderTest }], rows);
    }

    [Theory]
    [InlineData("""{ "parties": [ """, "does not parse")]
    [InlineData("""[ { "name": "Cow Service" } ]""", "not a JSON object")]
    [InlineData("""{ "partiesFormatVersion": 2, "parties": [] }""", "partiesFormatVersion is 2")]
    [InlineData("""{ "partiesFormatVersion": 1 }""", "no \"parties\" array")]
    public void A_file_that_cannot_be_used_gives_one_diagnostic_and_no_rows(string text, string why)
    {
        var (rows, findings) = PartiesFile.Parse(text, "parties.json");

        Assert.Empty(rows);
        var finding = Assert.Single(findings);
        Assert.Equal(DiagnosticKind.PartiesFileUnusable, finding.Kind);
        Assert.Contains(why, finding.Message);
    }

    [Fact]
    public void A_file_the_option_names_that_does_not_exist_is_reported()
    {
        var (rows, findings) = PartiesFile.Read(Path.Combine(_root, "missing.json"));

        Assert.Empty(rows);
        Assert.Contains("does not exist", Assert.Single(findings, f => f.Kind == DiagnosticKind.PartiesFileUnusable).Message);
    }

    [Theory]
    [InlineData("stub")]
    [InlineData("3")]
    public void A_kind_that_is_none_of_the_five_is_left_undeclared_and_said(string kind)
    {
        var (rows, findings) = PartiesFile.Parse($$"""{ "parties": [ { "name": "Cow Service", "id": "cow", "kind": "{{kind}}" } ] }""", "parties.json");

        Assert.Equal([new PartyDescriptor("Cow Service") { Id = "cow" }], rows);
        Assert.Contains($"\"{kind}\"", Assert.Single(findings, f => f.Kind == DiagnosticKind.PartyDeclarationInvalid).Message);
    }

    [Fact]
    public void A_name_the_file_lists_twice_and_a_row_with_no_name_are_said_by_the_resolver()
    {
        var (rows, findings) = PartiesFile.Parse("""{ "parties": [ { "name": "Cow Service" }, { "name": "Cow Service" }, { "id": "nameless" } ] }""", "parties.json");

        var table = PartyResolver.Resolve([new PartyCall("Caller", "Cow Service")], new PartyDeclarations(null, [], rows, findings), PartyObservations.None);

        Assert.Single(table.Diagnostics, d => d.Kind == DiagnosticKind.PartyDeclarationConflict);
        Assert.Single(table.Diagnostics, d => d.Kind == DiagnosticKind.PartyDeclarationInvalid);
    }
}
