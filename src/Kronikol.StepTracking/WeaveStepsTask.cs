using System;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Kronikol.StepTracking;

/// <summary>
/// MSBuild task that runs after compilation and uses Mono.Cecil to instrument
/// methods decorated with [GivenStep], [WhenStep], [ThenStep], [Step] attributes
/// with StepCollector.StartStep/CompleteStep calls.
/// Only activates if [assembly: TrackSteps] is found in the compiled assembly. Warns (KRONIKOL004) when it leaves a step
/// method as the compiler wrote it, and (KRONIKOL005) when a SkipIf cannot be used and the step runs regardless.
/// </summary>
public class WeaveStepsTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public string AssemblyPath { get; set; } = string.Empty;

    /// <summary>The project's PdbFile property: symbols it writes somewhere other than beside the assembly.</summary>
    public string? PdbFile { get; set; }

    /// <summary>
    /// Resolved reference paths (from MSBuild @(ReferencePath)). Used to configure
    /// Cecil's assembly resolver so it can locate referenced assemblies.
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
            Log.LogMessage(MessageImportance.Low, "StepTracking: Assembly not found at {0}", AssemblyPath);
            return true;
        }

        // A project that embeds its PDB (DebugType=embedded) or writes none (DebugType=none) has no file of this build's
        // beside the assembly: the weaver reads what symbols there are from the assembly itself, and a step needs none to be
        // woven. The build writes a PdbFile with its extension changed to .pdb, whatever it was given.
        string? pdbPath = Path.ChangeExtension(!string.IsNullOrEmpty(PdbFile) ? PdbFile : AssemblyPath, ".pdb");
        if (!File.Exists(pdbPath))
            pdbPath = null;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var searchDirectories = References
            .Select(r => Path.GetDirectoryName(r.ItemSpec))
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var weaver = new StepWeaver(Log, searchDirectories!);
        var result = weaver.Weave(AssemblyPath, pdbPath);
        sw.Stop();

        // A step method the weave cannot bracket is left as the compiler wrote it; it still runs, without a step. The
        // build says so once, as a warning a project can silence with <NoWarn>, naming how many and the first.
        if (result.UnwovenSteps.Count > 0)
        {
            var first = result.UnwovenSteps
                .OrderBy(u => u.Document, StringComparer.Ordinal).ThenBy(u => u.Line).First();
            var unreadSkipIfs = result.UnwovenSteps.Count(u => u.Reason.EndsWith(StepWeaver.SkipIfNotRead, StringComparison.Ordinal));
            Log.LogWarning(subcategory: null, warningCode: "KRONIKOL004", helpKeyword: null, file: null,
                lineNumber: 0, columnNumber: 0, endLineNumber: 0, endColumnNumber: 0,
                message: "Kronikol.StepTracking: {0} step method(s) were left as written, so they run without a step. "
                    + "First: {1}. {2}The build lists each one at normal verbosity (-v:n).",
                result.UnwovenSteps.Count, first,
                unreadSkipIfs switch
                {
                    0 => "",
                    1 => "1 of them carries a SkipIf that is not read, so its body runs whatever it says. ",
                    _ => $"{unreadSkipIfs} of them carry a SkipIf that is not read, so their bodies run whatever it says. ",
                });
            Log.LogMessage(MessageImportance.Normal,
                "Kronikol.StepTracking: Left {0} step method(s) unwoven; they run as written, without a step:",
                result.UnwovenSteps.Count);
            foreach (var unwoven in result.UnwovenSteps)
                Log.LogMessage(MessageImportance.Normal, "  {0}", unwoven);
        }

        foreach (var noBody in result.StepsWithNoBody)
            Log.LogMessage(MessageImportance.Normal, "Kronikol.StepTracking: A step attribute did nothing: {0}", noBody);

        if (result.WeavedCount > 0)
        {
            Log.LogMessage(MessageImportance.Normal,
                "Kronikol.StepTracking: Instrumented {0} step method(s) ({1}ms)",
                result.WeavedCount, sw.ElapsedMilliseconds);
        }
        else
        {
            Log.LogMessage(MessageImportance.Low,
                "Kronikol.StepTracking: Completed in {0}ms (no step methods found{1})",
                sw.ElapsedMilliseconds,
                result.SkipReason != null ? $" - {result.SkipReason}" : "");
        }

        return true;
    }
}
