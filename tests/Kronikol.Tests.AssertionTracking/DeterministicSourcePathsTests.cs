using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using Kronikol.AssertionTracking;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Mono.Cecil;
using Mono.Cecil.Cil;
using ModuleDefinition = Mono.Cecil.ModuleDefinition;

namespace Kronikol.Tests.AssertionTracking;

/// <summary>
/// A build with deterministic source paths (#165, WEAVER_SOURCE_PATHS_PLAN section 5.1). <c>ContinuousIntegrationBuild</c>
/// turns them on, and the compiler then writes each document of the PDB as <c>/_/…</c>, a name no machine has, so the
/// weave read no statement: every note was "Assertion at line N", with no values, and the statements a pragma comment
/// disables were woven. The weave reads each document where the compiler read it: through the build's own map, checked
/// against the PDB's checksum, else from the source the PDB embeds. Each fixture here is compiled by the compiler's own
/// resolver with the map, as <c>csc /pathmap</c> does (<see cref="TestAssemblyBuilder.BuildMapped"/>).
/// </summary>
public class DeterministicSourcePathsTests
{
    private static readonly char Separator = Path.DirectorySeparatorChar;

    // The probe's #163 statements (harness probe/Scenarios/IssueFeature.cs), each passing; three are disabled by pragmas.
    private const string Issue = """
        using System.Collections.Generic;
        using System.Linq;
        using FluentAssertions;
        using Kronikol.Tracking;
        using Shouldly;

        [assembly: TrackAssertions]

        public enum Colour { Red, Blue }

        public record Order(string Id, Colour Colour, List<string> Lines);

        public record Split(string SplitId);

        public static class IssueCases
        {
            public static void Statements()
            {
                var actual = new Order("A1", Colour.Blue, new List<string> { "x", "y" });
                var expected = new Order("A1", Colour.Blue, new List<string> { "x", "y" });
                var splits = new List<Split> { new("S1") };
                var result = new Split("S1");

                actual.Id.ShouldBe(expected.Id);
                actual.Colour.ToString().ShouldBe(expected.Colour.ToString());
                actual.Lines.First().ShouldBe(expected.Lines.First());
                actual.Colour.ToString().ToLowerInvariant().ShouldBe(expected.Colour.ToString().ToLowerInvariant());
                actual.Colour.ToString().Should().Be(expected.Colour.ToString());
                result.SplitId.ShouldBe(splits.First().SplitId);
                actual.Id.Should().Be(expected.Id);
                actual.Id.Should().Be("A1"); // pragma:TrackAssertions:disable
                // pragma:TrackAssertions:disable
                actual.Lines.Count.ShouldBe(2);
                expected.Id.Should().Be("A1");
                // pragma:TrackAssertions:enable
                actual.Lines.Last().ShouldBe("y");
            }
        }
        """;

    // Eleven assertion statements, three of them disabled.
    private const int IssueWoven = 8;

    private static KeyValuePair<string, string> Entry(string key, string value) => new(key, value);

    private static string Under(string root, params string[] parts) => Path.Combine([root, .. parts]);

    private static string WithSeparator(string directory) => directory.EndsWith(Separator) ? directory : directory + Separator;

    /// <summary>Weaves with the map the task hands the weaver (its <c>PathMap</c> entries and source roots).</summary>
    private static WeaveResult Weave(string assemblyPath, IReadOnlyList<KeyValuePair<string, string>> map, Encoding? codePage = null) =>
        WovenIl.Weave(new AssertionWeaver(null, null, map, codePage), assemblyPath);

    private static WovenRun.Note[] Notes(string assemblyPath, string type = "IssueCases", string method = "Statements")
    {
        var (thrown, notes) = WovenRun.Run(assemblyPath, type, method);
        Assert.Null(thrown);
        return notes;
    }

    private static string[] DocumentNames(string assemblyPath)
    {
        var pdb = Path.ChangeExtension(assemblyPath, ".pdb");
        using var module = ModuleDefinition.ReadModule(assemblyPath, File.Exists(pdb)
            ? new ReaderParameters { ReadSymbols = true, SymbolReaderProvider = new PortablePdbReaderProvider() }
            : new ReaderParameters { ReadSymbols = true, SymbolReaderProvider = new EmbeddedPortablePdbReaderProvider() });
        return module.GetTypes().SelectMany(t => t.Methods)
            .Where(m => m.DebugInformation.HasSequencePoints)
            .SelectMany(m => m.DebugInformation.SequencePoints.Select(sp => sp.Document.Url))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>The same statements compiled and woven with no map: what a local build draws.</summary>
    private static WovenRun.Note[] LocalNotes(string name, string source, OptimizationLevel optimization = OptimizationLevel.Debug)
    {
        var root = TestAssemblyBuilder.NewSourceRoot(name);
        var path = TestAssemblyBuilder.BuildMapped(name, [TestAssemblyBuilder.FixtureFile.Utf8(Under(root, "tests", "IssueCases.cs"), source)], [], optimization);
        Weave(path, []);
        return Notes(path, SourceType(source));
    }

    private static string SourceType(string source) => source.Contains("class IssueCases") ? "IssueCases" : "Cases";

    // Fact 1.
    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void A_mapped_build_labels_each_statement_as_a_local_build_does(OptimizationLevel optimization)
    {
        var root = TestAssemblyBuilder.NewSourceRoot($"Mapped1{optimization}");
        var map = new[] { Entry(WithSeparator(root), "/_/") };
        var path = TestAssemblyBuilder.BuildMapped($"Mapped1{optimization}",
            [TestAssemblyBuilder.FixtureFile.Utf8(Under(root, "tests", "IssueCases.cs"), Issue)], map, optimization);
        Assert.Equal(["/_/tests/IssueCases.cs"], DocumentNames(path));

        var result = Weave(path, map);

        var notes = Notes(path);
        Assert.Equal(LocalNotes($"Local1{optimization}", Issue, optimization), notes);
        Assert.Equal(IssueWoven, result.WeavedCount);
        Assert.DoesNotContain(notes, n => n.Label.StartsWith("Assertion at line", StringComparison.Ordinal));
        Assert.Contains(notes, n => n.Label == "Actual id should be 'A1'");
        Assert.Contains(notes, n => n.Label == "Actual colour should be 'Blue'");
        Assert.Empty(result.UnreadSources);
    }

    // Fact 2.
    [Fact]
    public void A_mapped_build_reads_the_pragma_comments()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("Mapped2");
        var map = new[] { Entry(WithSeparator(root), "/_/") };
        var path = TestAssemblyBuilder.BuildMapped("Mapped2",
            [TestAssemblyBuilder.FixtureFile.Utf8(Under(root, "tests", "IssueCases.cs"), Issue)], map);

        var result = Weave(path, map);

        // The inline pragma's statement and the two in the disabled block are left as written; the one after the block's
        // enable is woven.
        Assert.Equal(IssueWoven, result.WeavedCount);
        var labels = Notes(path).Select(n => n.Label).ToArray();
        Assert.Equal(IssueWoven, labels.Length);
        Assert.DoesNotContain(labels, l => l.StartsWith("Actual lines count", StringComparison.Ordinal));
        Assert.DoesNotContain(labels, l => l.StartsWith("Expected id", StringComparison.Ordinal));
        Assert.Equal("Last value of actual lines should be \"y\"", labels[^1]);
    }

    private const string OneStatement = """
        using FluentAssertions;
        using Kronikol.Tracking;

        [assembly: TrackAssertions]

        public static class Cases
        {
            public static void Statements()
            {
                var total = 3;
                total.Should().Be(3);
            }
        }
        """;

    private const string OtherStatement = """
        using FluentAssertions;
        using Kronikol.Tracking;

        [assembly: TrackAssertions]

        public static class Cases
        {
            public static void Statements()
            {
                var total = 3;
                total.Should().BeGreaterThan(2);
            }
        }
        """;

    // Fact 5: two directories mapped to one name, a file of that name in each; the checksum tells the compiled one.
    [Fact]
    public void Two_directories_mapped_to_one_name_are_told_apart_by_the_checksum()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("Mapped5");
        var compiled = Under(root, "a", "Cases.cs");
        var other = Under(root, "b", "Cases.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        File.WriteAllText(other, OtherStatement);

        // The other directory's entry first, as written: without the checksum it would be read.
        var map = new[] { Entry(WithSeparator(Under(root, "b")), "/src/"), Entry(WithSeparator(Under(root, "a")), "/src/") };
        var path = TestAssemblyBuilder.BuildMapped("Mapped5", [TestAssemblyBuilder.FixtureFile.Utf8(compiled, OneStatement)], map);
        Assert.Equal(["/src/Cases.cs"], DocumentNames(path));

        Weave(path, map);

        Assert.Equal("Total should be 3", Assert.Single(Notes(path, "Cases")).Label);
    }

    // Fact 5, the overlapping form: /src/ and /src/sub/.
    [Fact]
    public void Overlapping_mapped_values_are_told_apart_by_the_checksum()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("Mapped5Overlap");
        var compiled = Under(root, "a", "sub", "Cases.cs");
        var other = Under(root, "b", "Cases.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        File.WriteAllText(other, OtherStatement);

        // a/sub/Cases.cs is /src/sub/Cases.cs; so would b/Cases.cs be, and its entry's value is the longer.
        var map = new[] { Entry(WithSeparator(Under(root, "a")), "/src/"), Entry(WithSeparator(Under(root, "b")), "/src/sub/") };
        var path = TestAssemblyBuilder.BuildMapped("Mapped5Overlap", [TestAssemblyBuilder.FixtureFile.Utf8(compiled, OneStatement)], map);
        Assert.Equal(["/src/sub/Cases.cs"], DocumentNames(path));

        Weave(path, map);

        Assert.Equal("Total should be 3", Assert.Single(Notes(path, "Cases")).Label);
    }

    // Fact 6: a #line target has no checksum; of two candidates, the longer mapped value's file is read.
    [Fact]
    public void A_document_without_a_checksum_is_read_through_the_longer_mapped_value()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("Mapped6");
        var target = Under(root, "b", "Lines.cs");
        var decoy = Under(root, "a", "sub", "Lines.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.CreateDirectory(Path.GetDirectoryName(decoy)!);

        // The statement's columns are counted in Cases.cs; the #line target holds it at the same columns on its line 1.
        const string statement = "        total.Should().Be(3);";
        File.WriteAllText(target, statement + "\n");
        File.WriteAllText(decoy, "        total.Should().BeGreaterThan(2);\n");
        var source = $$"""
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public static class Cases
            {
                public static void Statements()
                {
                    var total = 3;
            #line 1 "{{target}}"
            {{statement}}
            #line default
                }
            }
            """;

        var map = new[] { Entry(WithSeparator(Under(root, "a")), "/src/"), Entry(WithSeparator(Under(root, "b")), "/src/sub/") };
        var path = TestAssemblyBuilder.BuildMapped("Mapped6", [TestAssemblyBuilder.FixtureFile.Utf8(Under(root, "a", "Cases.cs"), source)], map);
        using (var module = ModuleDefinition.ReadModule(path, new ReaderParameters { ReadSymbols = true }))
        {
            var document = module.GetType("Cases").Methods.Single(m => m.Name == "Statements").DebugInformation.SequencePoints
                .Select(sp => sp.Document).Single(d => d.Url.EndsWith("Lines.cs", StringComparison.Ordinal));
            Assert.Equal("/src/sub/Lines.cs", document.Url);
            Assert.Equal(DocumentHashAlgorithm.None, document.HashAlgorithm);
        }

        Weave(path, map);

        Assert.Equal("Total should be 3", Assert.Single(Notes(path, "Cases")).Label);
    }

    // Fact 7: the PathMap parser against the compiler's own, on the same strings.
    [Theory]
    [InlineData("k=v")]
    [InlineData("k/=v/")]
    [InlineData(@"k\=v\")]
    [InlineData("k=v,")]
    [InlineData(",k=v")]
    [InlineData("k=v,,,w=x")]
    [InlineData("a,,b==c=v")]
    [InlineData("a=b=c")]
    [InlineData("=v")]
    [InlineData("k=")]
    [InlineData("k=v,,w=x")]
    [InlineData(@"C:\Users\me\.nuget\packages\=/_1/,C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\=/_2/,C:\work\repo\=/_/,")]
    [InlineData("/home/me/.nuget/packages/=/_1/,/home/me/repo/=/_/,/home/me/repo/sub/=/_/sub/")]
    [InlineData(@"C:/mixed\dir=/src")]
    [InlineData("noseparators=either")]
    public void The_PathMap_is_parsed_as_the_compiler_parses_it(string pathMap)
    {
        var compiler = CSharpCommandLineParser.Default.Parse([$"/pathmap:{pathMap}", "a.cs"], Path.GetTempPath(), sdkDirectory: null);
        var refused = new List<string>();

        var parsed = SourceDocuments.ParsePathMap(pathMap, refused);

        static IEnumerable<string> Sorted(IEnumerable<KeyValuePair<string, string>> map) =>
            map.Select(e => e.Key + "\u0000" + e.Value).Order(StringComparer.Ordinal);
        Assert.Equal(Sorted(compiler.PathMap), Sorted(parsed));
        Assert.Equal(compiler.Errors.Count(e => e.Id == "CS8101"), refused.Count);
    }

    // Fact 7: the compiler turns every separator of the mapped name to the value's kind; the weave turns them back.
    [Fact]
    public void A_mapped_value_written_with_the_other_separator_is_read_back()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("Mapped7");
        var key = WithSeparator(root);
        var value = Separator == '/' ? @"\src\" : "/src/";
        var map = new[] { Entry(key, value) };
        var path = TestAssemblyBuilder.BuildMapped("Mapped7",
            [TestAssemblyBuilder.FixtureFile.Utf8(Under(root, "one", "two", "Cases.cs"), OneStatement)], map);
        Assert.Equal([value + "one" + value[0] + "two" + value[0] + "Cases.cs"], DocumentNames(path));

        Weave(path, map);

        Assert.Equal("Total should be 3", Assert.Single(Notes(path, "Cases")).Label);
    }

    // Fact 8.
    [Fact]
    public void Embedded_source_is_read_when_no_file_is_there()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("Mapped8");
        var map = new[] { Entry(WithSeparator(root), "/_/") };
        var path = TestAssemblyBuilder.BuildMapped("Mapped8",
            [TestAssemblyBuilder.FixtureFile.Utf8(Under(root, "tests", "IssueCases.cs"), Issue) with { Embedded = true, OnDisk = false }], map);
        Assert.False(File.Exists(Under(root, "tests", "IssueCases.cs")));

        var result = Weave(path, map);

        Assert.Equal(IssueWoven, result.WeavedCount);
        Assert.Equal(LocalNotes("Local8", Issue), Notes(path));
        Assert.Contains(result.DiagMessages, m => m == "Read the source of /_/tests/IssueCases.cs from the source the PDB embeds");
    }

    // Fact 9: the file was rewritten after the compile, so it fails the checksum; the PDB's own copy is the compiled text.
    [Fact]
    public void Embedded_source_wins_over_a_file_that_fails_the_checksum()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("Mapped9");
        var map = new[] { Entry(WithSeparator(root), "/_/") };
        var path = TestAssemblyBuilder.BuildMapped("Mapped9",
            [TestAssemblyBuilder.FixtureFile.Utf8(Under(root, "Cases.cs"), OneStatement) with
            {
                Embedded = true,
                BytesAfterCompile = new UTF8Encoding(false).GetBytes(OtherStatement),
            }], map);

        Weave(path, map);

        Assert.Equal("Total should be 3", Assert.Single(Notes(path, "Cases")).Label);
    }

    // Fact 9: a file under the name the compiler wrote that fails the checksum, with no embedded source, is still read,
    // as every weave before R1 read it: a local build never reads less than it did.
    [Fact]
    public void A_file_under_its_own_name_that_fails_the_checksum_is_still_read()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("Unmapped9");
        var path = TestAssemblyBuilder.BuildMapped("Unmapped9",
            [TestAssemblyBuilder.FixtureFile.Utf8(Under(root, "Cases.cs"), OneStatement) with
            {
                // The same statement with another expected value, at the same columns.
                BytesAfterCompile = new UTF8Encoding(false).GetBytes(OneStatement.Replace("Be(3)", "Be(4)")),
            }], []);

        var result = Weave(path, []);

        Assert.Equal("Total should be 4", Assert.Single(Notes(path, "Cases")).Label);
        Assert.Contains(result.DiagMessages, m => m.StartsWith($"Read the source of {Under(root, "Cases.cs")} as written, though", StringComparison.Ordinal));
    }

    // Fact 9: a file with the checksum is read before embedded source, so the PDB's blob is copied, never re-deflated.
    [Fact]
    public void A_file_with_the_checksum_is_read_and_the_embedded_blob_stays_the_compilers()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("Mapped9Blob");
        var map = new[] { Entry(WithSeparator(root), "/_/") };
        var path = TestAssemblyBuilder.BuildMapped("Mapped9Blob",
            [TestAssemblyBuilder.FixtureFile.Utf8(Under(root, "tests", "IssueCases.cs"), Issue) with { Embedded = true }], map);
        var compilers = EmbeddedBlob(File.ReadAllBytes(Path.ChangeExtension(path, ".pdb")), "/_/tests/IssueCases.cs");

        var result = Weave(path, map);

        Assert.Contains(result.DiagMessages, m => m == $"Read the source of /_/tests/IssueCases.cs from {Under(root, "tests", "IssueCases.cs")}");
        Assert.Equal(compilers, EmbeddedBlob(File.ReadAllBytes(Path.ChangeExtension(path, ".pdb")), "/_/tests/IssueCases.cs"));
        Assert.Equal(IssueWoven, result.WeavedCount);
    }

    private static readonly Guid EmbeddedSourceKind = new("0E8A571B-6926-466E-B4AD-8AB04611F5FE");

    private static byte[] EmbeddedBlob(byte[] pdb, string document)
    {
        using var provider = MetadataReaderProvider.FromPortablePdbImage(ImmutableArray.Create(pdb));
        var reader = provider.GetMetadataReader();
        foreach (var handle in reader.CustomDebugInformation)
        {
            var information = reader.GetCustomDebugInformation(handle);
            if (reader.GetGuid(information.Kind) != EmbeddedSourceKind || information.Parent.Kind != HandleKind.Document)
                continue;
            if (reader.GetString(reader.GetDocument((DocumentHandle)information.Parent).Name) == document)
                return reader.GetBlobBytes(information.Value);
        }

        throw new InvalidOperationException($"No embedded source for {document}");
    }

    // Fact 10: a nested root (a submodule) is mapped under its container's name, and the top-level entry reads it.
    [Fact]
    public void A_nested_roots_document_is_read_through_the_top_level_entry()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("Mapped10");
        var map = new[] { Entry(WithSeparator(root), "/_/") };
        var path = TestAssemblyBuilder.BuildMapped("Mapped10",
            [TestAssemblyBuilder.FixtureFile.Utf8(Under(root, "external", "sub", "Cases.cs"), OneStatement)], map);
        Assert.Equal(["/_/external/sub/Cases.cs"], DocumentNames(path));

        Weave(path, map);

        Assert.Equal("Total should be 3", Assert.Single(Notes(path, "Cases")).Label);
    }

    // Fact 11: the repository is /_/ and a second root /_1/; each document is read from its own root.
    [Fact]
    public void Documents_under_two_roots_are_each_read_from_their_own()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("Mapped11");
        var first = Under(root, "repo");
        var second = Under(root, "packages");
        var map = new[] { Entry(WithSeparator(second), "/_1/"), Entry(WithSeparator(first), "/_/") };
        var path = TestAssemblyBuilder.BuildMapped("Mapped11",
        [
            TestAssemblyBuilder.FixtureFile.Utf8(Under(first, "Cases.cs"), OneStatement),
            TestAssemblyBuilder.FixtureFile.Utf8(Under(second, "More.cs"), """
                using FluentAssertions;

                public static class More
                {
                    public static void Statements()
                    {
                        var count = 2;
                        count.Should().Be(2);
                    }
                }
                """),
        ], map);
        Assert.Equal(["/_/Cases.cs", "/_1/More.cs"], DocumentNames(path).Order(StringComparer.Ordinal));

        Weave(path, map);

        Assert.Equal("Total should be 3", Assert.Single(Notes(path, "Cases")).Label);
        Assert.Equal("Count should be 2", Assert.Single(Notes(path, "More")).Label);
    }

    // Fact 12: the woven calls carry the name the compiler wrote, so the woven bytes do not depend on the clone's path.
    [Fact]
    public void The_woven_calls_and_the_pdb_keep_the_compilers_names()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("Mapped12");
        var map = new[] { Entry(WithSeparator(root), "/_/") };
        var path = TestAssemblyBuilder.BuildMapped("Mapped12",
            [TestAssemblyBuilder.FixtureFile.Utf8(Under(root, "tests", "IssueCases.cs"), Issue)], map);

        Weave(path, map);

        using (var module = ModuleDefinition.ReadModule(path))
        {
            var literals = module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions).Where(i => i.OpCode == OpCodes.Ldstr).Select(i => (string)i.Operand)
                .ToArray();
            Assert.Contains("/_/tests/IssueCases.cs", literals);
            Assert.DoesNotContain(literals, l => l.Contains(root, StringComparison.OrdinalIgnoreCase));
        }

        Assert.Equal(["/_/tests/IssueCases.cs"], DocumentNames(path));
        // The literals are UTF-16 in the assembly's string heap, at an even or an odd offset.
        var image = File.ReadAllBytes(path);
        Assert.DoesNotContain(root, Encoding.Unicode.GetString(image), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(root, Encoding.Unicode.GetString(image, 1, image.Length - 1), StringComparison.OrdinalIgnoreCase);
    }

    // Fact 13: nothing readable, so the label falls back as before, and the document is recorded with what was tried.
    [Fact]
    public void A_document_nothing_reaches_falls_back_and_is_recorded()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("Mapped13");
        var map = new[] { Entry(WithSeparator(root), "/_/") };
        var path = TestAssemblyBuilder.BuildMapped("Mapped13",
            [TestAssemblyBuilder.FixtureFile.Utf8(Under(root, "tests", "Cases.cs"), OneStatement) with { OnDisk = false }], map);

        var result = Weave(path, map);

        Assert.Equal("Assertion at line 11", Assert.Single(Notes(path, "Cases")).Label);
        var unread = Assert.Single(result.UnreadSources);
        Assert.Equal("/_/tests/Cases.cs", unread.Document);
        Assert.Equal([Under(root, "tests", "Cases.cs"), "/_/tests/Cases.cs"], unread.Tried);
        Assert.Equal(1, unread.Statements);
    }

    // Fact 13 as the plan states it (audit): no map entry matches the name, here a map handed to the weave for another
    // value, and no file or embedded text answers, so the name as written is the one path tried.
    [Fact]
    public void A_document_no_map_entry_matches_is_tried_under_its_own_name_alone()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("Mapped13Unmatched");
        var path = TestAssemblyBuilder.BuildMapped("Mapped13Unmatched",
            [TestAssemblyBuilder.FixtureFile.Utf8(Under(root, "tests", "Cases.cs"), OneStatement) with { OnDisk = false }],
            [Entry(WithSeparator(root), "/_/")]);

        var result = Weave(path, [Entry(WithSeparator(root), "/_1/")]);

        Assert.Equal("Assertion at line 11", Assert.Single(Notes(path, "Cases")).Label);
        var unread = Assert.Single(result.UnreadSources);
        Assert.Equal("/_/tests/Cases.cs", unread.Document);
        Assert.Equal(["/_/tests/Cases.cs"], unread.Tried);
        Assert.Equal(1, unread.Statements);
    }

    // Fact 14: the compiler ends a line at U+0085, U+2028 and U+2029, so the PDB's line numbers count them.
    [Theory]
    [InlineData('\u2028')]
    [InlineData('\u0085')]
    [InlineData('\u2029')]
    public void A_statement_after_a_unicode_line_break_is_labelled_with_its_own_text(char lineBreak)
    {
        var source = """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public static class Cases
            {
                // A comment that ends with a line break of Unicode's own:
            """ + lineBreak + """

                public static void Statements()
                {
                    var first = 1;
                    var second = 2;
                    first.Should().Be(1);
                    second.Should().Be(2);
                }
            }
            """;
        var root = TestAssemblyBuilder.NewSourceRoot($"Lines14_{(int)lineBreak:X4}");
        var path = TestAssemblyBuilder.BuildMapped($"Lines14_{(int)lineBreak:X4}", [TestAssemblyBuilder.FixtureFile.Utf8(Under(root, "Cases.cs"), source)], []);

        Weave(path, []);

        Assert.Equal(["First should be 1", "Second should be 2"], Notes(path, "Cases").Select(n => n.Label));
    }

    private static string Cafe(string comparison) => $$"""
        using FluentAssertions;
        using Kronikol.Tracking;

        [assembly: TrackAssertions]

        public static class Cases
        {
            public static void Statements()
            {
                var name = "{{comparison}}";
                name.Should().Be("{{comparison}}");
            }
        }
        """;

    // Fact 15: a file that is not UTF-8 is read as the compiler read it.
    [Fact]
    public void A_bom_less_windows_1252_file_is_read_as_the_compiler_reads_it()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("Ansi15");
        var bytes = CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetBytes(Cafe("café"));
        var path = TestAssemblyBuilder.BuildMapped("Ansi15", [new TestAssemblyBuilder.FixtureFile(Under(root, "Cases.cs"), bytes)], []);

        Weave(path, []);

        // The compiler's fallback is the machine's: Windows-1252 on the Windows machines this suite runs on, UTF-8 with
        // U+FFFD for the byte it cannot read on Linux and macOS (the audit, 2026-10-15).
        Assert.Equal(OperatingSystem.IsWindows() ? "Name should be \"café\"" : "Name should be \"caf\uFFFD\"",
            Assert.Single(Notes(path, "Cases")).Label);
    }

    // The audit (2026-10-15): with no byte order mark and no CodePage, a file that is not UTF-8 is read by the compiler
    // with the code page Roslyn's EncodedStringText.CreateFallbackEncoding asks for, 0, which is the ANSI code page on
    // Windows and UTF-8 with U+FFFD for each byte it cannot read on Linux and macOS (measured with the compiler on
    // both). The weave read Windows-1252 on every machine; a label showed é where the compiled text had U+FFFD, and a
    // pair of bytes that is UTF-8 was two characters to the weave and one to the compiler.
    [Theory]
    [InlineData(true, "caf\u00E9 \u00C3\u00A9")]
    [InlineData(false, "caf\uFFFD \u00E9")]
    public void A_file_that_is_not_utf8_is_read_with_the_platforms_fallback(bool onWindows, string text)
    {
        byte[] bytes = [(byte)'c', (byte)'a', (byte)'f', 0xE9, (byte)' ', 0xC3, 0xA9];

        Assert.Equal(text, SourceDocuments.Decode(bytes, null, onWindows));
    }

    // The audit: Roslyn reads a text of 40 K characters or more through a StreamReader, which knows UTF-32's byte order
    // marks, and a smaller one from its bytes, where FF FE is UTF-16's (measured with the compiler: a 64,200-byte UTF-32
    // file was read as UTF-32, and the weave read it as UTF-16).
    [Fact]
    public void A_utf32_file_is_read_as_the_compiler_reads_it_by_its_size()
    {
        var large = "// " + new string('x', 20_000) + "\nvar name = \"café\";";
        var little = new UTF32Encoding(bigEndian: false, byteOrderMark: true);
        var big = new UTF32Encoding(bigEndian: true, byteOrderMark: true);
        byte[] largeLittle = [.. little.GetPreamble(), .. little.GetBytes(large)];
        byte[] largeBig = [.. big.GetPreamble(), .. big.GetBytes(large)];
        byte[] small = [.. little.GetPreamble(), .. little.GetBytes("café")];

        Assert.Equal(large, SourceDocuments.Decode(largeLittle, null));
        Assert.Equal(large, SourceDocuments.Decode(largeBig, null));
        Assert.Equal(Encoding.Unicode.GetString(small, 2, small.Length - 2), SourceDocuments.Decode(small, null));
    }

    // The audit: the compiler opens a source sharing it for reading and writing; the weave asked that nobody be writing
    // it, so a file another process held open for writing (an editor, a watcher) was not read and fell back.
    [Fact]
    public void A_file_another_process_holds_open_for_writing_is_read()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("HeldOpen");
        var file = Under(root, "Cases.cs");
        var path = TestAssemblyBuilder.BuildMapped("HeldOpen", [TestAssemblyBuilder.FixtureFile.Utf8(file, OneStatement)], []);

        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            Weave(path, []);

        Assert.Equal("Total should be 3", Assert.Single(Notes(path, "Cases")).Label);
    }

    // The audit: a mapped file that is there but is not what the compiler read was listed among the paths tried as if it
    // were not there, so KRONIKOL002 named a path the reader could open and see.
    [Fact]
    public void A_mapped_file_that_changed_since_the_compile_is_named_so_among_the_paths_tried()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("Changed");
        var file = Under(root, "tests", "Cases.cs");
        var map = new[] { Entry(WithSeparator(root), "/_/") };
        var path = TestAssemblyBuilder.BuildMapped("Changed", [TestAssemblyBuilder.FixtureFile.Utf8(file, OneStatement)], map);
        File.AppendAllText(file, "// changed after the compile\n");

        var result = Weave(path, map);

        Assert.Equal("Assertion at line 11", Assert.Single(Notes(path, "Cases")).Label);
        var unread = Assert.Single(result.UnreadSources);
        Assert.Equal([file + " (changed since the compile)", "/_/tests/Cases.cs"], unread.Tried);
    }

    // Fact 15: with CodePage set, the compiler decodes every file without a byte order mark in it, valid UTF-8 included.
    [Fact]
    public void A_build_code_page_decodes_a_bom_less_utf8_file_as_the_compiler_does()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("CodePage15");
        var codePage = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
        var path = TestAssemblyBuilder.BuildMapped("CodePage15",
            [TestAssemblyBuilder.FixtureFile.Utf8(Under(root, "Cases.cs"), Cafe("café"))], [], codePage: codePage);

        Weave(path, [], codePage);

        // The compiler read the two bytes of é as Ã and ©, and the PDB's columns count them so.
        Assert.Equal("Name should be \"cafÃ©\"", Assert.Single(Notes(path, "Cases")).Label);
    }

    // Fact 16 (a guard): files with a byte order mark, and UTF-8 without one, read as before.
    [Theory]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-8-bom")]
    [InlineData("utf-8")]
    public void A_file_in_a_unicode_encoding_is_read_as_before(string encoding)
    {
        var text = Cafe("café");
        byte[] bytes = encoding switch
        {
            "utf-16" => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)],
            "utf-16BE" => [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(text)],
            "utf-8-bom" => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)],
            _ => new UTF8Encoding(false).GetBytes(text),
        };
        var root = TestAssemblyBuilder.NewSourceRoot($"Unicode16_{encoding}");
        var path = TestAssemblyBuilder.BuildMapped($"Unicode16_{encoding.Replace('-', '_')}",
            [new TestAssemblyBuilder.FixtureFile(Under(root, "Cases.cs"), bytes)], []);

        Weave(path, []);

        Assert.Equal("Name should be \"café\"", Assert.Single(Notes(path, "Cases")).Label);
    }

    // $(CodePage) as the compiler's /codepage takes it: a positive number; empty and 0 set none.
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("0", null)]
    [InlineData("1252", 1252)]
    // MSBuild hands Csc the property as a number, sign and spaces allowed (the audit, 2026-10-15: csc got /codepage:1252).
    [InlineData("+1252", 1252)]
    [InlineData(" 1252 ", 1252)]
    [InlineData("1251", 1251)]
    [InlineData("65001", 65001)]
    public void The_builds_code_page_is_read_as_the_compiler_reads_it(string? value, int? codePage)
    {
        var encoding = SourceDocuments.CodePageEncoding(value, out var note);

        Assert.Equal(codePage, encoding?.CodePage);
        Assert.Null(note);
    }

    [Fact]
    public void A_code_page_that_is_not_a_number_is_noted_and_not_followed()
    {
        Assert.Null(SourceDocuments.CodePageEncoding("utf-8", out var note));
        Assert.Equal("CodePage 'utf-8' is not a code page; the sources are read as in a build without one", note);
    }

    // The weave decodes Windows-1252 itself (MSBuild on .NET has no code pages unless one is registered): every byte as
    // the runtime's own decoder reads it.
    [Fact]
    public void The_weaves_windows_1252_is_the_runtimes()
    {
        var bytes = Enumerable.Range(0, 256).Select(b => (byte)b).ToArray();

        Assert.Equal(CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetString(bytes), SourceDocuments.Windows1252(bytes));
    }

    // Fact 17: Roslyn records which documents a type without method bodies lives in, by document row; the weave's rewrite
    // keeps only the documents with sequence points, so the records it copied named other documents, or other types.
    [Fact]
    public void The_woven_pdb_holds_no_type_record_that_names_another_types_document()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("Types17");
        var path = TestAssemblyBuilder.BuildMapped("Types17",
        [
            TestAssemblyBuilder.FixtureFile.Utf8(Under(root, "IOnly.cs"), """
                public interface IOnly
                {
                    void Run();
                }
                """),
            TestAssemblyBuilder.FixtureFile.Utf8(Under(root, "Cases.cs"), OneStatement),
            TestAssemblyBuilder.FixtureFile.Utf8(Under(root, "Other.cs"), """
                public class Other
                {
                    public int Value() => 1;
                }
                """),
        ], []);
        var compilers = TypeRecords(path);
        Assert.Contains("IOnly", compilers.Keys);

        Weave(path, []);

        foreach (var (type, documents) in TypeRecords(path))
            Assert.True(compilers.TryGetValue(type, out var expected) && expected.SequenceEqual(documents),
                $"{type} is recorded in {string.Join(", ", documents)}");
    }

    private static readonly Guid TypeDefinitionDocumentsKind = new("932E74BC-DBA9-4478-8D46-0F32A7BAB3D3");

    /// <summary>Each type's <c>TypeDefinitionDocuments</c> record, read back with System.Reflection.Metadata: the type's
    /// name and the names of the documents the record's row numbers point at.</summary>
    private static Dictionary<string, string[]> TypeRecords(string assemblyPath)
    {
        using var pe = new PEReader(ImmutableArray.Create(File.ReadAllBytes(assemblyPath)));
        var metadata = pe.GetMetadataReader();
        using var provider = MetadataReaderProvider.FromPortablePdbImage(ImmutableArray.Create(File.ReadAllBytes(Path.ChangeExtension(assemblyPath, ".pdb"))));
        var pdb = provider.GetMetadataReader();
        var records = new Dictionary<string, string[]>();
        foreach (var handle in pdb.CustomDebugInformation)
        {
            var information = pdb.GetCustomDebugInformation(handle);
            if (pdb.GetGuid(information.Kind) != TypeDefinitionDocumentsKind)
                continue;
            var type = metadata.GetTypeDefinition((TypeDefinitionHandle)information.Parent);
            var blob = pdb.GetBlobReader(information.Value);
            var documents = new List<string>();
            while (blob.RemainingBytes > 0)
            {
                var row = blob.ReadCompressedInteger();
                documents.Add(row <= pdb.Documents.Count
                    ? pdb.GetString(pdb.GetDocument(MetadataTokens.DocumentHandle(row)).Name)
                    : $"(row {row} of {pdb.Documents.Count})");
            }

            records[metadata.GetString(type.Name)] = [.. documents.Select(d => Path.GetFileName(d))];
        }

        return records;
    }
}
