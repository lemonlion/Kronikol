using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Kronikol.AssertionTracking;

/// <summary>
/// MSBuild task that runs after compilation and uses Mono.Cecil to instrument the assertion statements of
/// FluentAssertions and AwesomeAssertions (<c>.Should()</c>), Shouldly (<c>ShouldBe()</c> and the rest) and TUnit
/// (<c>Assert.That()</c>) with assertion tracking: a try/catch around each statement that calls
/// Track.AssertionPassed/Track.AssertionFailed. Only activates if <c>[assembly: TrackAssertions]</c> is found in the
/// compiled assembly, and warns (KRONIKOL001) when that attribute finds nothing to instrument. It finds the
/// statements through the build's symbols, a portable PDB beside the assembly or one embedded in it, and reads each
/// statement's text where the compiler read it: the file the PDB names, mapped back through <see cref="PathMap"/> and
/// <see cref="SourceRoots"/> when the build wrote other names (deterministic source paths, #165), or else the source
/// the PDB embeds.
/// </summary>
public class WeaveAssertionsTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public string AssemblyPath { get; set; } = string.Empty;

    /// <summary>
    /// The build's source roots (<c>@(SourceRoot)</c>). A root whose <c>MappedPath</c> metadata differs from its path
    /// is a name the compiler wrote for that directory, and a document under that name is read from the directory.
    /// </summary>
    public ITaskItem[] SourceRoots { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>
    /// The compiler's path map (<c>$(PathMap)</c>, its <c>/pathmap</c>), as the build passed it: <c>key=value</c>
    /// entries separated by <c>,</c>, with a doubled <c>,</c> or <c>=</c> standing for the character. Deterministic
    /// source paths (<c>ContinuousIntegrationBuild</c>) write each source root here as <c>/_/</c>, <c>/_1/</c> and on,
    /// and a project can add its own. A document named under a value is read from the key's directory.
    /// </summary>
    public string? PathMap { get; set; }

    /// <summary>
    /// The build's code page (<c>$(CodePage)</c>, the compiler's <c>/codepage</c>), or empty: the compiler decodes every
    /// source file without a byte order mark in it, and so does the weave.
    /// </summary>
    public string? CodePage { get; set; }

    /// <summary>
    /// Resolved reference paths (from MSBuild @(ReferencePath)). Used to configure
    /// Cecil's assembly resolver so it can locate referenced assemblies (NuGet packages, etc.).
    /// </summary>
    public ITaskItem[] References { get; set; } = Array.Empty<ITaskItem>();

    public override bool Execute()
    {
        try
        {
            return ExecuteCore();
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, showStackTrace: true);
            return false;
        }
    }

    private bool ExecuteCore()
    {
        if (!File.Exists(AssemblyPath))
        {
            Log.LogMessage(MessageImportance.Low, "AssertionTracking: Assembly not found at {0}", AssemblyPath);
            return true;
        }

        // A project that embeds its PDB (DebugType=embedded) has no file beside the assembly: the weaver reads
        // the symbols from the assembly itself, and leaves one with none at all (DebugType=none) unwoven.
        string? pdbPath = Path.ChangeExtension(AssemblyPath, ".pdb");
        if (!File.Exists(pdbPath))
            pdbPath = null;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var searchDirectories = References
            .Select(r => Path.GetDirectoryName(r.ItemSpec))
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var refused = new List<string>();
        var map = SourceDocuments.ParsePathMap(PathMap, refused);
        foreach (var root in SourceRoots)
        {
            var mapped = root.GetMetadata("MappedPath");
            if (!string.IsNullOrEmpty(mapped) && !string.Equals(mapped, root.ItemSpec, StringComparison.Ordinal))
                map.Add(new KeyValuePair<string, string>(root.ItemSpec, mapped));
        }

        var codePage = SourceDocuments.CodePageEncoding(CodePage, out var codePageNote);
        var weaver = new AssertionWeaver(Log, searchDirectories!, map, codePage);
        var result = weaver.Weave(AssemblyPath, pdbPath);
        sw.Stop();

        foreach (var entry in refused)
            Log.LogMessage(MessageImportance.Low, "AssertionTracking: PathMap entry '{0}' is not key=value; it is not read", entry);
        if (codePageNote != null)
            Log.LogMessage(MessageImportance.Low, "AssertionTracking: {0}", codePageNote);
        foreach (var message in result.DiagMessages)
            Log.LogMessage(MessageImportance.Low, "AssertionTracking: {0}", message);

        // A statement the weave cannot account for is left as the compiler wrote it, never miscompiled; it still
        // runs, untracked, and the build says where and why.
        if (result.Unwoven.Count > 0)
        {
            Log.LogMessage(MessageImportance.Normal,
                "Kronikol.AssertionTracking: Left {0} assertion statement(s) unwoven; they run as written, without a note:",
                result.Unwoven.Count);
            foreach (var unwoven in result.Unwoven)
                Log.LogMessage(MessageImportance.Normal, "  {0}", unwoven);
        }

        // [assembly: TrackAssertions] asks for notes; a weave that can give none says why, as a warning with a code
        // a project can silence with <NoWarn> (#144). It said so only at detailed verbosity, so a suite whose only
        // library was not read looked tracked and drew nothing.
        if (result.WeavedCount == 0 && NothingInstrumented(result) is { } reason)
        {
            Log.LogWarning(subcategory: null, warningCode: "KRONIKOL001", helpKeyword: null, file: null,
                lineNumber: 0, columnNumber: 0, endLineNumber: 0, endColumnNumber: 0,
                message: "Kronikol.AssertionTracking: [assembly: TrackAssertions] is declared, but no assertion was instrumented: {0}.",
                reason);
        }

        if (result.WeavedCount > 0)
        {
            Log.LogMessage(MessageImportance.Normal,
                "Kronikol.AssertionTracking: Instrumented {0} assertion(s) in {1} method(s) ({2}ms)",
                result.WeavedCount, result.MethodCount, sw.ElapsedMilliseconds);
        }
        else
        {
            Log.LogMessage(MessageImportance.Low,
                "Kronikol.AssertionTracking: Completed in {0}ms (no assertions found{1})",
                sw.ElapsedMilliseconds,
                result.SkipReason != null ? $" - {result.SkipReason}" : "");
        }

        return true;
    }

    private const string Libraries = "FluentAssertions, AwesomeAssertions, Shouldly or TUnit.Assertions";

    /// <summary>Why a weave with the attribute declared instrumented nothing, or null when it is not a case to
    /// warn about: no attribute, an assembly already woven, or statements the weave left unwoven and listed.</summary>
    private static string? NothingInstrumented(WeaveResult result) => result.SkipReason switch
    {
        "No assertion library referenced" =>
            $"the assembly references none of the libraries the weaver reads ({Libraries}); an assertion of another library can be drawn with Track.That(() => ...)",
        { } skip when skip.StartsWith("No symbols", StringComparison.Ordinal) =>
            "the build writes no symbols (<DebugType>none</DebugType>), and the weave finds each statement through them",
        null when result.Unwoven.Count == 0 =>
            $"no statement in the assembly calls an assertion of {Libraries}; an assertion of another library can be drawn with Track.That(() => ...)",
        _ => null,
    };
}
