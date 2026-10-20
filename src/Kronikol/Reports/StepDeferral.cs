using System.Globalization;
using Kronikol.InternalFlow;

namespace Kronikol.Reports;

/// <summary>
/// The sub-steps one page holds out of its markup until a reader opens their step (5.0.0, plans/REPORT_FORMAT_PLAN.md
/// 4.8 part b, Q8). Each entry is the HTML of a closed composite step's sub-steps, gzipped and base64-encoded, keyed from
/// the step's empty <c>div.sub-steps</c>; the page writes the map once, as <c>#step-data</c>, and
/// <c>report-sub-steps-on-expand.js</c> draws an entry the first time its step opens. One per page: the pages of a run are
/// written in parallel.
/// </summary>
internal sealed class StepDeferral
{
    /// <summary>
    /// A subtree's HTML of this many characters or more is held. Below it the entry costs more than it saves: base64 adds a
    /// third, and drawing it waits on a decompression.
    /// </summary>
    internal const int Threshold = 2048;

    /// <summary>The held subtrees by key, in the order the page meets them.</summary>
    public Dictionary<string, string> Map { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// True while a held subtree is being rendered: a closed composite step inside it goes into the same entry, so a chain
    /// of them is one entry and one decompression.
    /// </summary>
    public bool Inside { get; set; }

    /// <summary>Holds <paramref name="html"/> and returns the key the page writes on the empty sub-steps holder.</summary>
    public string Add(string html)
    {
        var key = "s" + (Map.Count + 1).ToString(CultureInfo.InvariantCulture);
        Map[key] = InternalFlowHtmlGenerator.CompressToBase64(html);
        return key;
    }
}
