using System;
using System.Collections.Generic;
using System.Linq;
using Kronikol.History;
using Kronikol.Tracking;

namespace Kronikol.Tests.History;

/// <summary>
/// The history guard (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.8): what a tracker records for one fixed call, read as the
/// call line history fingerprints, pinned with the rule it was pinned under. Linked into every tracker's test project,
/// so a change to what a tracker records fails that tracker's build unless it moves <see cref="InteractionShape.Version"/>,
/// which every such change must, with a bridge (the earlier rule kept as a function of the raw call, as
/// <c>RequestResponseLog.RecordedBefore8</c> keeps 6 and 7), a translation of the stored lines, or the lines neither
/// reaches declared. The 4 to 6 upgrade's gRPC and Cosmos DB changes shipped unseen for want of one.
/// </summary>
internal static class HistoryGuard
{
    /// <summary>
    /// Fails unless <paramref name="logs"/> make exactly <paramref name="lines"/> under rule <paramref name="version"/>, the
    /// rule the lines were pinned under. Throws its own exception rather than an assertion library's, so it reads the same
    /// in every test framework a tracker's project runs on.
    /// </summary>
    public static void Pin(IEnumerable<RequestResponseLog?> logs, int version, params string[] lines)
    {
        var actual = InteractionShape.Calls(logs).Select(c => c.ToString()).ToArray();
        var spelled = string.Join(Environment.NewLine, actual.Select(line => "    \"" + line.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\""));
        if (InteractionShape.Version != version)
            throw new HistoryGuardException(
                $"History's rule moved from {version} to {InteractionShape.Version}. Keep rule {version}'s lines for this capture in the core " +
                $"corpus (ShapeGuardTests), then pin what the capture makes under {InteractionShape.Version}:{Environment.NewLine}{spelled}");
        if (!actual.SequenceEqual(lines, StringComparer.Ordinal))
            throw new HistoryGuardException(
                $"What this tracker records moved its history call lines under rule {version}. Such a change must move " +
                "InteractionShape.Version and declare a bridge (the earlier rule kept as a function of the raw call), a translation of the " +
                "stored lines, or the lines neither reaches (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.8); otherwise every scenario that " +
                $"makes this call reads as changed once. Pinned:{Environment.NewLine}{string.Join(Environment.NewLine, lines.Select(l => "    " + l))}" +
                $"{Environment.NewLine}Now:{Environment.NewLine}{spelled}");
    }
}

/// <summary>A guard's failure: a call line that moved without its rule moving.</summary>
internal sealed class HistoryGuardException(string message) : Exception(message);
