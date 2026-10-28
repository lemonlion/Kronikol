using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Kronikol.Tests.AssertionTracking;

/// <summary>
/// Compiles C# source code into a test assembly with PDB for the weaver tests.
/// </summary>
public static class TestAssemblyBuilder
{
    private static readonly string OutputDir = Path.Combine(
        Path.GetTempPath(), "KRONIKOL_WeaverTests", Guid.NewGuid().ToString("N")[..8]);

    // Fixture project directory (sibling to test source files)
    private static readonly string FixturesDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures");

    static TestAssemblyBuilder()
    {
        Directory.CreateDirectory(OutputDir);
    }

    // Auto-generated attribute sources (matches what the .targets file generates)
    private const string AttributeSource = """
        using System;
        namespace Kronikol.Tracking
        {
            [AttributeUsage(AttributeTargets.Assembly)]
            internal sealed class TrackAssertionsAttribute : Attribute { }

            // Keep old name for backward compat tests
            [AttributeUsage(AttributeTargets.Assembly)]
            internal sealed class TrackAssertionsBetaAttribute : Attribute { }

            [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = true)]
            internal sealed class SuppressAssertionTrackingAttribute : Attribute { }
        }
        """;

    /// <summary>
    /// Compiles source code into an assembly with PDB. Returns the assembly file path.
    /// The assembly references FluentAssertions and Kronikol for realistic assertion tracking.
    /// </summary>
    public static string Build(string name, string source, OptimizationLevel optimization = OptimizationLevel.Debug) =>
        Build(name, source, optimization, DebugInformationFormat.PortablePdb);

    /// <summary>
    /// Compiles source code with its symbols in the given form: a portable PDB file beside the assembly,
    /// a portable PDB embedded in it (<c>DebugType=embedded</c>), or none at all when
    /// <paramref name="symbols"/> is null (<c>DebugType=none</c>).
    /// </summary>
    public static string Build(string name, string source, OptimizationLevel optimization, DebugInformationFormat? symbols) =>
        Build(name, source, optimization, symbols, []);

    /// <summary>
    /// Compiles source code that also references <paramref name="extraReferences"/> (assembly paths), such as the
    /// real TUnit.Assertions, which the fixtures with TUnit stubs must not see.
    /// </summary>
    public static string Build(string name, string source, OptimizationLevel optimization, DebugInformationFormat? symbols,
        IReadOnlyList<string> extraReferences) =>
        Build(name, source, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithOptimizationLevel(optimization),
            symbols, extraReferences);

    /// <summary>
    /// The ways a test project's own build compiles it. <see cref="Release"/> is how xUnit v3, TUnit, MSTest, NUnit and
    /// a plain library build Release (csc's <c>/optimize+ /debug-</c>, with a portable PDB).
    /// <see cref="ReleaseDebugPlus"/> is xUnit v2's Release: its props set <c>DebugSymbols</c>, so csc gets
    /// <c>/debug+ /optimize+</c>, Roslyn's debug-plus mode, which keeps a closure or an awaited result in a local of
    /// its own where plain Release keeps it on the stack (AWAITED_ASSERTION_VALUES_PLAN 3.7). Debug compiles the same
    /// either way.
    /// </summary>
    public enum BuildConfiguration
    {
        Debug,
        Release,
        ReleaseDebugPlus
    }

    /// <summary>Compiles source code as <paramref name="configuration"/> builds it, with a portable PDB beside the
    /// assembly.</summary>
    public static string Build(string name, string source, BuildConfiguration configuration,
        IReadOnlyList<string>? extraReferences = null) =>
        Build(name, source, configuration switch
        {
            BuildConfiguration.Debug => new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Debug),
            BuildConfiguration.Release => new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release),
            // Debug-plus is internal to Roslyn's options; csc's own parser is the public way to it.
            _ => CSharpCommandLineParser.Default.Parse(["/debug+", "/debug:portable", "/optimize+", "/target:library"],
                OutputDir, sdkDirectory: null).CompilationOptions,
        }, DebugInformationFormat.PortablePdb, extraReferences ?? []);

    private static string Build(string name, string source, CSharpCompilationOptions options, DebugInformationFormat? symbols,
        IReadOnlyList<string> extraReferences)
    {
        var assemblyPath = Path.Combine(OutputDir, $"{name}.dll");
        var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");

        var sourcePath = Path.Combine(OutputDir, $"{name}.cs");
        var sourceText = SourceText.From(source, Encoding.UTF8);
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText,
            new CSharpParseOptions(LanguageVersion.Latest),
            path: sourcePath);

        // Write the source file to disk so the weaver can read it for expression text
        File.WriteAllText(sourcePath, source, Encoding.UTF8);

        // Include the auto-generated attributes so [assembly: TrackAssertions] compiles
        var attrSourceText = SourceText.From(AttributeSource, Encoding.UTF8);
        var attrTree = CSharpSyntaxTree.ParseText(attrSourceText,
            new CSharpParseOptions(LanguageVersion.Latest));

        var references = GetReferences()
            .Concat(extraReferences.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            name,
            new[] { syntaxTree, attrTree },
            references,
            options);

        using var peStream = File.Create(assemblyPath);
        EmitResult result;
        if (symbols == DebugInformationFormat.PortablePdb)
        {
            using var pdbStream = File.Create(pdbPath);
            result = compilation.Emit(peStream, pdbStream,
                options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        }
        else if (symbols is { } format)
        {
            result = compilation.Emit(peStream, options: new EmitOptions(debugInformationFormat: format));
        }
        else
        {
            result = compilation.Emit(peStream);
        }

        if (!result.Success)
        {
            var errors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.GetMessage());
            throw new InvalidOperationException(
                $"Compilation failed for '{name}':\n{string.Join("\n", errors)}");
        }

        return assemblyPath;
    }

    /// <summary>
    /// One source file of a fixture built by <see cref="BuildMapped"/>: where it is, the bytes the compiler reads, and
    /// what the weave finds there afterwards.
    /// </summary>
    /// <param name="Path">The file's full path.</param>
    /// <param name="Bytes">The bytes the compiler reads and hashes.</param>
    public sealed record FixtureFile(string Path, byte[] Bytes)
    {
        /// <summary>The PDB embeds the file's source, as under <c>EmbedAllSources</c>.</summary>
        public bool Embedded { get; init; }

        /// <summary>What the file holds when the weave reads it: its compiled bytes unless set, nothing when
        /// <see cref="OnDisk"/> is false.</summary>
        public byte[]? BytesAfterCompile { get; init; }

        /// <summary>False when no file is left at <see cref="Path"/> after the compile.</summary>
        public bool OnDisk { get; init; } = true;

        /// <summary>A UTF-8 file with no byte order mark, as an editor writes one.</summary>
        public static FixtureFile Utf8(string path, string text) => new(path, new UTF8Encoding(false).GetBytes(text));
    }

    /// <summary>A fresh directory for one fixture's source files, under this run's output directory.</summary>
    public static string NewSourceRoot(string name)
    {
        var root = Path.Combine(OutputDir, name + "_src");
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// Compiles <paramref name="files"/> as <c>csc</c> does in a build that passes <paramref name="pathMap"/> to its
    /// <c>/pathmap</c>, as deterministic source paths (<c>ContinuousIntegrationBuild</c>) and a project's own
    /// <c>PathMap</c> do (#165): each PDB document is the file's path mapped by the compiler's own resolver, its checksum
    /// is the SHA-256 of the file's bytes, and the bytes are decoded as the compiler decodes them (a byte order mark;
    /// else <paramref name="codePage"/>; else strict UTF-8, and Windows-1252 when they are not UTF-8). Returns the
    /// assembly's path; the files are left on disk as each <see cref="FixtureFile"/> says.
    /// </summary>
    public static string BuildMapped(
        string name, IReadOnlyList<FixtureFile> files, IReadOnlyList<KeyValuePair<string, string>> pathMap,
        OptimizationLevel optimization = OptimizationLevel.Debug,
        DebugInformationFormat symbols = DebugInformationFormat.PortablePdb, Encoding? codePage = null,
        SourceHashAlgorithm checksum = SourceHashAlgorithm.Sha256)
    {
        var assemblyPath = Path.Combine(OutputDir, $"{name}.dll");
        var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        if (File.Exists(pdbPath))
            File.Delete(pdbPath);

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var texts = files.Select(f => (File: f, Text: AsCompilerReadsIt(f.Bytes, codePage, checksum, f.Embedded))).ToList();
        var trees = texts.Select(t => CSharpSyntaxTree.ParseText(t.Text, parseOptions, path: t.File.Path))
            .Append(CSharpSyntaxTree.ParseText(SourceText.From(AttributeSource, Encoding.UTF8), parseOptions))
            .ToArray();

        // The compiler sorts its map by key, the longest first (CommandLineParser.SortPathMap), before handing it to the
        // resolver that names each document.
        var resolver = new SourceFileResolver(
            [], Path.GetDirectoryName(files[0].Path),
            [.. pathMap.OrderByDescending(e => e.Key.Length)]);
        var compilation = CSharpCompilation.Create(
            name,
            trees,
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(optimization)
                .WithSourceReferenceResolver(resolver));

        var embedded = texts.Where(t => t.File.Embedded).Select(t => EmbeddedText.FromSource(t.File.Path, t.Text)).ToArray();
        EmitResult result;
        using (var peStream = File.Create(assemblyPath))
        {
            if (symbols == DebugInformationFormat.PortablePdb)
            {
                using var pdbStream = File.Create(pdbPath);
                result = compilation.Emit(peStream, pdbStream,
                    options: new EmitOptions(debugInformationFormat: symbols), embeddedTexts: embedded);
            }
            else
            {
                result = compilation.Emit(peStream,
                    options: new EmitOptions(debugInformationFormat: symbols), embeddedTexts: embedded);
            }
        }

        if (!result.Success)
            throw new InvalidOperationException($"Compilation failed for '{name}':\n" + string.Join("\n",
                result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));

        foreach (var file in files)
        {
            if (file.OnDisk)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                File.WriteAllBytes(file.Path, file.BytesAfterCompile ?? file.Bytes);
            }
            else if (File.Exists(file.Path))
            {
                File.Delete(file.Path);
            }
        }

        return assemblyPath;
    }

    /// <summary>The text the compiler makes of a file's bytes (Roslyn's <c>EncodedStringText.Create</c>): a byte order
    /// mark wins; else the build's code page; else strict UTF-8, and on bytes that are not UTF-8 the code page Roslyn's
    /// <c>CreateFallbackEncoding</c> asks for, 0: the machine's ANSI code page on Windows (1252 on the machines this
    /// suite runs on), and on Linux and macOS, which have none, UTF-8 with U+FFFD for each byte it cannot read.</summary>
    public static SourceText AsCompilerReadsIt(byte[] bytes, Encoding? codePage, SourceHashAlgorithm checksum, bool embedded)
    {
        if (codePage is not null)
            return SourceText.From(bytes, bytes.Length, codePage, checksum, throwIfBinaryDetected: false, canBeEmbedded: embedded);
        try
        {
            return SourceText.From(bytes, bytes.Length, new UTF8Encoding(false, throwOnInvalidBytes: true), checksum,
                throwIfBinaryDetected: false, canBeEmbedded: embedded);
        }
        catch (DecoderFallbackException)
        {
            return SourceText.From(bytes, bytes.Length, CodePagesEncodingProvider.Instance.GetEncoding(0) ?? new UTF8Encoding(false), checksum,
                throwIfBinaryDetected: false, canBeEmbedded: embedded);
        }
    }

    private static MetadataReference[] GetReferences()
    {
        var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Include all trusted platform assemblies (covers all runtime deps)
        var trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (trustedAssemblies != null)
        {
            foreach (var path in trustedAssemblies.Split(Path.PathSeparator))
            {
                if (File.Exists(path))
                    refs.Add(path);
            }
        }

        // FluentAssertions
        var faAssembly = typeof(FluentAssertions.AssertionExtensions).Assembly;
        refs.Add(faAssembly.Location);

        // FluentAssertions dependencies
        foreach (var referencedAssembly in faAssembly.GetReferencedAssemblies())
        {
            try
            {
                var loaded = Assembly.Load(referencedAssembly);
                if (!string.IsNullOrEmpty(loaded.Location))
                    refs.Add(loaded.Location);
            }
            catch { /* skip unresolvable */ }
        }

        // AwesomeAssertions (fork of FluentAssertions, uses different namespace)
        var aaAssembly = typeof(AwesomeAssertions.AssertionExtensions).Assembly;
        refs.Add(aaAssembly.Location);

        foreach (var referencedAssembly in aaAssembly.GetReferencedAssemblies())
        {
            try
            {
                var loaded = Assembly.Load(referencedAssembly);
                if (!string.IsNullOrEmpty(loaded.Location))
                    refs.Add(loaded.Location);
            }
            catch { /* skip unresolvable */ }
        }

        // Kronikol (for Track type and attributes)
        var ttdAssembly = typeof(Kronikol.Tracking.Track).Assembly;
        refs.Add(ttdAssembly.Location);

        return refs
            .Where(File.Exists)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray();
    }

    /// <summary>
    /// Compiles source code by shelling out to 'dotnet build' with a specific SDK version.
    /// This produces IL patterns from that SDK's Roslyn compiler, testing cross-compiler compatibility.
    /// </summary>
    /// <param name="name">Assembly name (used for file naming)</param>
    /// <param name="source">C# source code to compile</param>
    /// <param name="sdkVersion">SDK version to pin (e.g. "9.0.0")</param>
    /// <param name="tfm">Target framework (e.g. "net9.0")</param>
    /// <param name="configuration">Debug or Release</param>
    /// <param name="dotnetPath">Optional custom dotnet path (for preview SDKs installed outside Program Files)</param>
    /// <param name="packageVersions">Another version of a package the fixture project references.</param>
    /// <param name="properties">MSBuild properties for the build (<c>DebugSymbols=true</c> makes a Release build
    /// xUnit v2's debug-plus one). Each build runs in a project directory named after <paramref name="name"/>, so two
    /// builds that differ only by a property need two names: with one <c>obj/</c>, the second skips its compile.</param>
    /// <returns>Path to the compiled assembly DLL</returns>
    public static string BuildWithSdk(
        string name, string source, string sdkVersion, string tfm,
        string configuration = "Debug", string? dotnetPath = null,
        IReadOnlyDictionary<string, string>? packageVersions = null,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        var fixturesDir = Path.GetFullPath(FixturesDir);
        if (!File.Exists(Path.Combine(fixturesDir, "WeaverFixture.csproj")))
            throw new InvalidOperationException($"Fixtures project not found at {fixturesDir}");

        // Write the source file
        var sourceFile = Path.Combine(OutputDir, $"{name}.cs");
        File.WriteAllText(sourceFile, source, Encoding.UTF8);

        // Create a temporary global.json to pin the SDK version
        var buildDir = Path.Combine(OutputDir, $"{name}_build");
        Directory.CreateDirectory(buildDir);

        var globalJson = Path.Combine(buildDir, "global.json");
        File.WriteAllText(globalJson, $$"""
            {
              "sdk": {
                "version": "{{sdkVersion}}",
                "rollForward": "latestFeature",
                "allowPrerelease": true
              }
            }
            """);

        // Create a Directory.Build.props to prevent inheriting the repo's
        File.WriteAllText(Path.Combine(buildDir, "Directory.Build.props"),
            "<Project><PropertyGroup><LangVersion>preview</LangVersion></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(buildDir, "Directory.Build.targets"), "<Project />");

        var dotnetExe = dotnetPath ?? "dotnet";

        // Copy the fixture csproj and attributes to the build directory
        // (dotnet resolves global.json from the project directory, not working directory)
        var csprojPath = Path.Combine(buildDir, "WeaverFixture.csproj");
        File.Copy(Path.Combine(fixturesDir, "WeaverFixture.csproj"), csprojPath, overwrite: true);
        // Another version of a package the fixture project references (Shouldly 5's preview, say).
        if (packageVersions is { Count: > 0 })
        {
            var project = File.ReadAllText(csprojPath);
            foreach (var (package, version) in packageVersions)
                project = System.Text.RegularExpressions.Regex.Replace(project,
                    $"(<PackageReference Include=\"{System.Text.RegularExpressions.Regex.Escape(package)}\" Version=\")[^\"]+(\")",
                    "${1}" + version + "${2}");
            File.WriteAllText(csprojPath, project);
        }
        File.Copy(Path.Combine(fixturesDir, "Attributes.cs"),
            Path.Combine(buildDir, "Attributes.cs"), overwrite: true);

        var psi = new ProcessStartInfo
        {
            FileName = dotnetExe,
            Arguments = $"build \"{csprojPath}\" -c {configuration} " +
                        $"/p:FixtureSource=\"{Path.GetFullPath(sourceFile)}\" " +
                        $"/p:TargetFramework={tfm} " +
                        $"/p:OutputPath=\"{Path.GetFullPath(buildDir)}\" " +
                        string.Concat((properties ?? new Dictionary<string, string>()).Select(p => $"/p:{p.Key}={p.Value} ")) +
                        $"--no-restore",
            WorkingDirectory = buildDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Restore first (separate step to get packages)
        var restorePsi = new ProcessStartInfo
        {
            FileName = dotnetExe,
            Arguments = $"restore \"{csprojPath}\" " +
                        $"/p:TargetFramework={tfm}",
            WorkingDirectory = buildDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Clear inherited MSBuild env vars that override SDK resolution
        // (the test host sets these for the outer SDK, breaking inner builds)
        foreach (var envVar in new[] { "MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildExtensionsPath" })
        {
            psi.Environment.Remove(envVar);
            restorePsi.Environment.Remove(envVar);
        }

        var restoreProcess = Process.Start(restorePsi)!;
        var restoreOutput = restoreProcess.StandardOutput.ReadToEnd();
        var restoreError = restoreProcess.StandardError.ReadToEnd();
        restoreProcess.WaitForExit();

        if (restoreProcess.ExitCode != 0)
            throw new InvalidOperationException(
                $"dotnet restore failed for '{name}' (SDK {sdkVersion}):\n{restoreOutput}\n{restoreError}");

        var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"dotnet build failed for '{name}' (SDK {sdkVersion}, {configuration}):\n{stdout}\n{stderr}");

        // Find the output DLL
        var dllPath = Path.Combine(buildDir, "WeaverFixture.dll");
        if (!File.Exists(dllPath))
            throw new FileNotFoundException(
                $"Build output not found at {dllPath}. stdout:\n{stdout}");

        // Copy to a uniquely-named file so we can load multiple SDK builds
        var targetPath = Path.Combine(OutputDir, $"{name}.dll");
        File.Copy(dllPath, targetPath, overwrite: true);

        var pdbSource = Path.ChangeExtension(dllPath, ".pdb");
        var pdbTarget = Path.ChangeExtension(targetPath, ".pdb");
        if (File.Exists(pdbSource))
            File.Copy(pdbSource, pdbTarget, overwrite: true);

        return targetPath;
    }

    /// <summary>
    /// Checks if a given SDK version is available locally.
    /// </summary>
    public static bool IsSdkAvailable(string sdkMajorMinor, string? dotnetPath = null)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = dotnetPath ?? "dotnet",
                Arguments = "--list-sdks",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output.Contains($"{sdkMajorMinor}.");
        }
        catch
        {
            return false;
        }
    }
}
