using System.Text;
using System.Xml.Linq;

namespace Kronikol.Reports;

/// <summary>
/// Writes <c>query.cs</c> beside a report: <c>kronikol query</c> for a machine where the tool is not
/// installed and cannot be (roadmap stage 1b, <c>plans/QUERY_FALLBACK_PLAN.md</c>).
///
/// <para>An agent whose session may not install a tool or reach the NuGet feed meets Kronikol on its first
/// run, and the report is too large to read: without the tool it had no way past <c>Failures.md</c>.
/// <c>dotnet run --file query.cs -- summary .</c> runs the engine that wrote the report, so its answers are
/// the tool's by construction, not by a second implementation kept in step.</para>
///
/// <para><b>Why it restores nothing.</b> The plan's design was a <c>#:package Kronikol@version</c>
/// directive. Measured on SDK 10.0.401 against a consumer's NuGet cache with the feed unreachable, that
/// failed twice over: a file-based app defaults to native AOT, whose restore fetches
/// <c>Microsoft.DotNet.ILCompiler</c>, which no consumer has; and a net8.0 or net9.0 test project never
/// restored the net10.0 dependency group <c>Kronikol</c> would need. Even where it worked it printed
/// NuGet's NU1603 into the answer on its first run. So the file loads <c>Kronikol.dll</c> itself: the test
/// run's own copy when that is a .NET 10 build, which carries the engine, else the <c>lib/net10.0</c> copy
/// in the NuGet cache, which every consumer holds whatever it targets, because NuGet caches whole
/// packages. Either is used only at exactly the version that wrote the report. The cache is where
/// <c>NUGET_PACKAGES</c> or the default puts it, and where neither holds the engine the file asks NuGet
/// (<c>dotnet nuget locals global-packages --list</c>), since a <c>NuGet.config</c> can move it.</para>
///
/// <para><b>Why the properties.</b> A file-based app is built with the <c>Directory.Build.props</c> and
/// <c>Directory.Packages.props</c> of the folders above it, and a <c>#:property</c> lands after them, so it
/// overrides what they set. A <c>Directory.Build.targets</c> comes after the properties, so the file imports
/// none. Each one stops a failure seen under a repository's own settings: AOT fetches a package, a
/// <c>TargetFramework</c> of net8.0 retargets the app to a runtime the machine may not have, warnings as
/// errors, analyzers or a NuGet audit fail the build or print into the answer, and a repository's targets
/// run their own checks on it. The loop JIT setting is the tool's own, for the same speed.</para>
///
/// <para><b>What it holds.</b> Paths and a version, nothing a test produced. The run's own copy is named by
/// a path relative to the file, never an absolute one, which would carry a user's home directory into a
/// folder that is often published.</para>
/// </summary>
internal static class QueryScriptGenerator
{
    internal const string FileName = "query.cs";

    /// <summary>How the file begins, which is also how a later run knows the <c>query.cs</c> it finds is its own to replace.</summary>
    internal const string Marker = "// Written by Kronikol ";

    /// <summary>The file for the <c>Kronikol</c> this process runs, written into <paramref name="reportsDirectory"/>.</summary>
    internal static string Build(string reportsDirectory) =>
        Build(ReportGenerator.KronikolVersion.Split('+')[0],
            typeof(QueryScriptGenerator).Assembly.GetName().Version?.ToString() ?? "0.0.0.0",
            OwnCopy(reportsDirectory));

    /// <summary>
    /// The file's text. <paramref name="packageVersion"/> names the NuGet cache folder,
    /// <paramref name="assemblyVersion"/> is what a candidate <c>Kronikol.dll</c> must declare, and
    /// <paramref name="ownCopy"/> is the run's own <c>Kronikol.dll</c> relative to the reports directory, or
    /// null when the running build does not carry the engine.
    /// </summary>
    internal static string Build(string packageVersion, string assemblyVersion, string? ownCopy)
    {
        var version = packageVersion;
        var folder = packageVersion.ToLowerInvariant();
        var own = ownCopy is null
            ? ""
            : $"    System.IO.Path.GetFullPath(System.IO.Path.Combine(here, {Verbatim(ownCopy)})),\n";

        return $$"""
            {{Marker}}{{version}} beside the report in this directory, and rewritten on every run. It is
            // `kronikol query` for a machine where the tool is not installed and cannot be. From this directory:
            //
            //     dotnet run --file query.cs -- summary .
            //     dotnet run --file query.cs -- failures .
            //
            // It prints what `kronikol query` prints, because it runs the same engine: the Kronikol {{version}}
            // that wrote the report, loaded from where the test run left it. It installs, restores and downloads
            // nothing, and needs the .NET 10 SDK. The first run compiles it, in a few seconds; later runs start at
            // once. Where a global.json pins an older SDK, run it from outside that folder with full paths.
            //
            // The properties keep a repository's own build settings out: native AOT would fetch a package, a
            // pinned TargetFramework would retarget it, warnings as errors, analyzers or a NuGet audit would fail
            // it or print into its answer, and a Directory.Build.targets would come after all of them.
            #:property PublishAot=false
            #:property TargetFramework=net$(BundledNETCoreAppTargetFrameworkVersion)
            #:property OutputType=Exe
            #:property LangVersion=latest
            #:property TreatWarningsAsErrors=false
            #:property WarningsAsErrors=
            #:property WarningLevel=0
            #:property RunAnalyzers=false
            #:property NuGetAudit=false
            #:property ImportDirectoryBuildTargets=false
            #:property TieredCompilationQuickJitForLoops=false
            #nullable enable

            // The stdout the tool sets up: UTF-8 without a BOM, because an answer's byte budget is counted in UTF-8
            // and its addresses are joined with · and ›.
            try
            {
                System.Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            }
            catch (System.IO.IOException)
            {
            }
            catch (System.PlatformNotSupportedException)
            {
            }

            string here = System.IO.Path.GetDirectoryName(System.AppContext.GetData("EntryPointFilePath") as string ?? QueryScript.File()) ?? ".";
            string? configured = System.Environment.GetEnvironmentVariable("NUGET_PACKAGES");
            string packages = string.IsNullOrEmpty(configured)
                ? System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".nuget", "packages")
                : configured;

            // Where the engine can be: the test run's own copy, then the NuGet cache. Each is taken only at exactly
            // the version that wrote the report.
            var candidates = new System.Collections.Generic.List<string>
            {
            {{own}}    System.IO.Path.Combine(packages, "kronikol", "{{folder}}", "lib", "net10.0", "Kronikol.dll"),
            };

            foreach (string candidate in candidates)
                if (QueryScript.IsTheEngine(candidate, "{{assemblyVersion}}"))
                    return QueryScript.Run(candidate, args);

            // A NuGet.config can move the cache, and then neither NUGET_PACKAGES nor the default says where it is.
            // Once every other place has missed, NuGet itself is asked, from this folder, so it reads the settings
            // the test run restored under.
            if (QueryScript.NuGetCache(here) is { } moved)
            {
                string elsewhere = System.IO.Path.Combine(moved, "kronikol", "{{folder}}", "lib", "net10.0", "Kronikol.dll");
                if (!candidates.Contains(elsewhere))
                {
                    candidates.Add(elsewhere);
                    if (QueryScript.IsTheEngine(elsewhere, "{{assemblyVersion}}"))
                        return QueryScript.Run(elsewhere, args);
                }
            }

            System.Console.Error.WriteLine("query.cs: Kronikol {{version}}, which wrote this report, is not on this machine. It was looked for at:");
            foreach (string candidate in candidates)
                System.Console.Error.WriteLine("  " + candidate);
            System.Console.Error.WriteLine("Restore the test project on this machine (dotnet restore) and run this again, or ask the published tool,");
            System.Console.Error.WriteLine("which needs the NuGet feed:");
            System.Console.Error.WriteLine("  dnx Kronikol.Tool@{{version}} query " + QueryScript.Join(args));
            return 1;

            internal static class QueryScript
            {
                internal static string File([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

                internal static int Run(string engine, string[] args)
                {
                    System.Reflection.MethodInfo run = System.Reflection.Assembly.LoadFrom(engine)
                        .GetType("Kronikol.Query.QueryCommand", throwOnError: true)!
                        .GetMethod("Run", new[] { typeof(System.Collections.Generic.IReadOnlyList<string>), typeof(System.IO.TextWriter), typeof(System.IO.TextWriter) })!;
                    return (int)run.Invoke(null, System.Reflection.BindingFlags.DoNotWrapExceptions, null, new object[] { args, System.Console.Out, System.Console.Error }, null)!;
                }

                // `dotnet nuget locals global-packages --list`, asked from this folder with the SDK this file runs on:
                // NuGet's own answer, from NUGET_PACKAGES or the NuGet.config files above the folder. Null when it
                // cannot be asked or does not say.
                internal static string? NuGetCache(string here)
                {
                    try
                    {
                        string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
                        string host = System.IO.Path.Combine(root, System.OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
                        var start = new System.Diagnostics.ProcessStartInfo(System.IO.File.Exists(host) ? host : "dotnet", "nuget locals global-packages --list")
                        {
                            WorkingDirectory = here,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                        };
                        start.Environment["DOTNET_NOLOGO"] = "1";
                        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
                        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(start)!;
                        System.Threading.Tasks.Task<string> error = process.StandardError.ReadToEndAsync();
                        string output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();
                        error.Wait();
                        foreach (string line in output.Split('\n'))
                            if (line.StartsWith("global-packages:", System.StringComparison.Ordinal))
                                return line.Substring("global-packages:".Length).Trim();
                        return null;
                    }
                    catch (System.Exception)
                    {
                        return null;
                    }
                }

                internal static bool IsTheEngine(string path, string version)
                {
                    try
                    {
                        return System.IO.File.Exists(path) && System.Reflection.AssemblyName.GetAssemblyName(path).Version?.ToString() == version;
                    }
                    catch (System.Exception)
                    {
                        return false;
                    }
                }

                internal static string Join(string[] args) =>
                    string.Join(" ", System.Array.ConvertAll(args, a => a.Length == 0 || a.IndexOfAny(new[] { ' ', '"' }) >= 0 ? "\"" + a.Replace("\"", "\\\"") + "\"" : a));
            }

            """;
    }

    /// <summary>
    /// The run's own <c>Kronikol.dll</c>, relative to <paramref name="reportsDirectory"/> and with forward
    /// slashes, when this build carries the engine (.NET 10 and later) and a relative path reaches it; null
    /// otherwise, and then the NuGet cache is the only place the file looks.
    /// </summary>
    internal static string? OwnCopy(string reportsDirectory)
    {
#if NET10_0_OR_GREATER
        var location = typeof(QueryScriptGenerator).Assembly.Location;
        return string.IsNullOrEmpty(location)
            ? null
            : Portable(Path.GetRelativePath(Path.GetFullPath(reportsDirectory), location));
#else
        return null;
#endif
    }

    /// <summary>
    /// <paramref name="relative"/> as the file names it, with forward slashes, or null when it is not relative:
    /// on Windows a path to another drive has no relative form, and <see cref="Path.GetRelativePath"/> hands it
    /// back whole, home directory and all.
    /// </summary>
    internal static string? Portable(string relative) => Path.IsPathRooted(relative) ? null : relative.Replace('\\', '/');

    /// <summary>
    /// The C# project that would compile a <c>query.cs</c> written into <paramref name="directory"/>, or null
    /// when none would. A project takes in every <c>.cs</c> file below its folder except under its <c>bin</c>
    /// and <c>obj</c> and in a folder whose name starts with a dot, and the file's <c>#:</c> directives are an
    /// error in a project build, so written there it would break the consumer's build. Every folder up to the
    /// root is checked, since a project anywhere above counts. The reports directory is usually not created yet
    /// when this is asked, and a folder that cannot be listed is stepped over rather than taken as the answer.
    /// </summary>
    internal static string? CompilingProject(string directory)
    {
        var target = Path.GetFullPath(directory);
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        for (var folder = new DirectoryInfo(target); folder is not null; folder = folder.Parent)
        {
            if (!folder.Exists)
                continue;

            string[] projects;
            try
            {
                projects = Directory.GetFiles(folder.FullName, "*.csproj");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                continue;
            }

            if (projects.Length == 0)
                continue;

            var below = Path.GetRelativePath(folder.FullName, target)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(below[0], "bin", comparison) || string.Equals(below[0], "obj", comparison))
                continue;

            if (projects.Order(StringComparer.Ordinal).FirstOrDefault(p => !LeavesOut(p, below, comparison)) is { } compiling)
                return compiling;
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="project"/> plainly keeps a folder out of its build, the folder named by
    /// <paramref name="below"/> from the project's own folder down: default compile items off, a folder on the
    /// way whose name starts with a dot (the default items leave out
    /// <c>**/.*/**</c>, for <c>.git</c> and <c>.vs</c>), or a <c>Compile Remove</c> of the folder's tree or of
    /// everything. Nested layouts have to say one of these, or they compile the inner project's sources. Only
    /// what cannot be read any other way counts: a <c>Compile Include</c> with a <c>**</c> that could reach
    /// below the project's folder takes the file back whatever else the project says, and an element under a
    /// <c>Condition</c> or a file that is not XML is not trusted, so the project is taken to compile it.
    /// </summary>
    private static bool LeavesOut(string project, string[] below, StringComparison comparison)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(project);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return false;
        }

        static bool Unconditioned(XElement element) => element.AncestorsAndSelf().All(e => e.Attribute("Condition") is null);

        // A glob that starts above the project's folder, such as ..\Shared\**, never reaches below it.
        static bool Reaches(string pattern)
        {
            var glob = pattern.Trim().Replace('\\', '/');
            return glob.Contains("**", StringComparison.Ordinal) && !glob.StartsWith("../", StringComparison.Ordinal) && !Path.IsPathRooted(glob);
        }

        var elements = document.Descendants().ToArray();
        var compile = elements.Where(e => e.Name.LocalName == "Compile").ToArray();
        if (compile.Any(e => (e.Attribute("Include")?.Value ?? "").Split(';').Any(Reaches)))
            return false;

        if (elements.Any(e => e.Name.LocalName is "EnableDefaultCompileItems" or "EnableDefaultItems"
                              && Unconditioned(e) && string.Equals(e.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase)))
            return true;

        if (below.Any(name => name.Length > 1 && name[0] == '.' && name != ".."))
            return true;

        var folder = below[0];
        return compile.Where(Unconditioned)
            .SelectMany(e => (e.Attribute("Remove")?.Value ?? "").Split(';'))
            .Select(pattern => pattern.Trim().Replace('\\', '/'))
            .Any(pattern => pattern is "**" or "**/*" or "**/*.cs"
                            || string.Equals(pattern, folder + "/**", comparison)
                            || string.Equals(pattern, folder + "/**/*", comparison)
                            || string.Equals(pattern, folder + "/**/*.cs", comparison));
    }

    /// <summary>
    /// The <see cref="DiagnosticKind.OptionNotApplied"/> message for a directory <see cref="CompilingProject"/>
    /// ruled out. It names the project file and no path: a diagnostic is written into the report, which is
    /// often published.
    /// </summary>
    internal static string NotApplied(string project) =>
        $"{nameof(ReportConfigurationOptions.WriteQueryScript)} has no effect here: the reports directory is inside the folder of " +
        $"{Path.GetFileName(project)}, which compiles every .cs file below it except under bin, obj and a folder whose name starts " +
        $"with a dot, so a query.cs written there would break its build. Point {nameof(ReportConfigurationOptions.ReportsFolderPath)} " +
        $"under bin or obj, or into a folder whose name starts with a dot (such as .logs/kronikol), to have one, or set " +
        $"{nameof(ReportConfigurationOptions.WriteQueryScript)} = false to silence this.";

    /// <summary>
    /// Writes <paramref name="text"/> as <c>query.cs</c> through <paramref name="write"/>, unless a
    /// <c>query.cs</c> Kronikol did not write is already there: that one is left exactly as it is, and the
    /// throw is what tells the caller's isolated output list the file was not written.
    /// </summary>
    internal static void Write(string directory, string text, Action<string> write)
    {
        if (IsForeign(directory))
            throw new InvalidOperationException($"the {FileName} there was not written by Kronikol, or cannot be read, so it is left as it is");

        write(text);
    }

    /// <summary>
    /// Whether <paramref name="directory"/> holds a <c>query.cs</c> that Kronikol did not write. A run and a merge
    /// ask this before their outputs, outside the isolation that keeps one failed output from stopping the rest,
    /// so it never throws: a <c>query.cs</c> that cannot be read is taken as somebody else's.
    /// </summary>
    internal static bool IsForeign(string directory)
    {
        var path = Path.Combine(directory, FileName);
        return File.Exists(path) && !IsOurs(path);
    }

    private static bool IsOurs(string path)
    {
        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadLine()?.StartsWith(Marker, StringComparison.Ordinal) == true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>A C# verbatim string literal holding <paramref name="value"/>, whatever it holds.</summary>
    private static string Verbatim(string value) => "@\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
