using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kronikol.AssertionRewriter;

/// <summary>
/// MSBuild task that rewrites source files containing .Should() assertion statements
/// to wrap them in Track.That() calls. Only activates if [assembly: TrackAssertions]
/// is found in any source file.
/// </summary>
public class RewriteAssertionsTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public ITaskItem[] SourceFiles { get; set; } = Array.Empty<ITaskItem>();

    [Required]
    public string IntermediateDir { get; set; } = string.Empty;

    [Output]
    public ITaskItem[] RewrittenFiles { get; set; } = Array.Empty<ITaskItem>();

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
        var sourceFiles = SourceFiles.Select(item => item.ItemSpec).ToList();

        // Fast-path: scan for [assembly: TrackAssertions] in any file
        if (!HasTrackAssertionsAttribute(sourceFiles))
        {
            // No attribute found — pass through all files unchanged
            RewrittenFiles = SourceFiles;
            return true;
        }

        Directory.CreateDirectory(IntermediateDir);

        var rewrittenItems = new List<ITaskItem>();
        var totalWrapped = 0;

        foreach (var item in SourceFiles)
        {
            var filePath = item.ItemSpec;

            // Skip files that don't contain "Should" (fast text pre-filter)
            if (!FileContainsShould(filePath))
            {
                rewrittenItems.Add(item);
                continue;
            }

            // Parse and rewrite
            var sourceText = File.ReadAllText(filePath);
            var tree = CSharpSyntaxTree.ParseText(sourceText);
            var root = tree.GetRoot();
            var rewriter = new AssertionWrappingRewriter { OriginalFilePath = filePath };
            var newRoot = rewriter.Visit(root);

            if (rewriter.ChangeCount == 0)
            {
                rewrittenItems.Add(item);
                continue;
            }

            // Write rewritten file to intermediate directory
            var rewrittenPath = Path.Combine(IntermediateDir, RewrittenFileNames.Of(filePath));
            File.WriteAllText(rewrittenPath, newRoot.ToFullString());

            var newItem = new TaskItem(rewrittenPath);
            item.CopyMetadataTo(newItem);
            rewrittenItems.Add(newItem);
            totalWrapped += rewriter.ChangeCount;
        }

        if (totalWrapped > 0)
        {
            Log.LogMessage(MessageImportance.Normal,
                $"Kronikol.AssertionRewriter: Wrapped {totalWrapped} assertion(s) in Track.That()");
        }

        RewrittenFiles = rewrittenItems.ToArray();
        return true;
    }

    private static bool HasTrackAssertionsAttribute(List<string> filePaths)
    {
        foreach (var filePath in filePaths)
        {
            try
            {
                var content = File.ReadAllText(filePath);
                if (content.Contains("[assembly: TrackAssertions]") ||
                    content.Contains("[assembly:TrackAssertions]"))
                    return true;
            }
            catch
            {
                // Skip unreadable files
            }
        }

        return false;
    }

    private static bool FileContainsShould(string filePath)
    {
        try
        {
            var content = File.ReadAllText(filePath);
            return content.Contains(".Should()");
        }
        catch
        {
            return false;
        }
    }
}
