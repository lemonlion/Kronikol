using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Kronikol.AssertionTracking;

/// <summary>
/// MSBuild task that runs after compilation and uses Mono.Cecil to instrument the assertion statements of
/// FluentAssertions and AwesomeAssertions (<c>.Should()</c>) and TUnit (<c>Assert.That()</c>) with assertion
/// tracking: a try/catch around each statement that calls Track.AssertionPassed/Track.AssertionFailed.
/// Only activates if <c>[assembly: TrackAssertions]</c> is found in the compiled assembly. It reads the
/// statements from the build's symbols, a portable PDB beside the assembly or one embedded in it.
/// </summary>
public class WeaveAssertionsTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public string AssemblyPath { get; set; } = string.Empty;

    /// <summary>
    /// Root paths for source files, used to locate source code referenced by PDB sequence points.
    /// </summary>
    public ITaskItem[] SourceRoots { get; set; } = Array.Empty<ITaskItem>();

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
        var weaver = new AssertionWeaver(Log, searchDirectories!);
        var result = weaver.Weave(AssemblyPath, pdbPath);
        sw.Stop();

        foreach (var message in result.DiagMessages)
            Log.LogMessage(MessageImportance.Low, "AssertionTracking: {0}", message);

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
}
