using java.io;
using net.sourceforge.plantuml;
using net.sourceforge.plantuml.dot;

namespace Kronikol;

/// <summary>
/// Provides local PlantUML rendering using the IKVM Java bridge, converting PlantUML source text to PNG or SVG images.
/// </summary>
public static class IkvmPlantUmlRenderer
{
    /// <summary>
    /// Whether Graphviz is usable in this process, asked once.
    ///
    /// <para><see cref="GraphvizUtils.getDotVersion"/> answers <c>-1</c> when it cannot run <c>dot</c> at
    /// all — no executable at the resolved path, or one it cannot get a version out of. Either way the
    /// engine cannot lay a graph out, which is the only thing this decides.</para>
    /// </summary>
    private static readonly Lazy<bool> DotIsUsable = new(() =>
    {
        try { return GraphvizUtils.getDotVersion() > 0; }
        catch { return false; }
    });

    /// <summary>
    /// The layout engine this process will draw graph-shaped diagrams with: <c>dot</c> when Graphviz is
    /// installed, <c>smetana</c> — PlantUML's own pure-Java port of it — when it is not.
    /// </summary>
    public static string LayoutEngine => DotIsUsable.Value ? "dot" : "smetana";

    public static byte[] Render(string plantUml, PlantUmlImageFormat format)
    {
        var fileFormat = format switch
        {
            PlantUmlImageFormat.Png or PlantUmlImageFormat.Base64Png => FileFormat.PNG,
            PlantUmlImageFormat.Svg or PlantUmlImageFormat.Base64Svg => FileFormat.SVG,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported image format for local rendering.")
        };

        var reader = new SourceStringReader(WithLayoutEngine(plantUml));
        var outputStream = new ByteArrayOutputStream();
        reader.outputImage(outputStream, new FileFormatOption(fileFormat));
        return outputStream.toByteArray();
    }

    /// <summary>
    /// Names a layout engine in the source when Graphviz is missing, and leaves the source alone when it
    /// is not.
    ///
    /// <para><b>Why this is needed at all.</b> Component, class and state diagrams are laid out by
    /// Graphviz. Without <c>dot</c> PlantUML does not fail — it returns a valid image of a small card
    /// reading "Cannot find Graphviz", so a caller gets a picture with no diagram in it and no error to
    /// act on. Kronikol's architecture overview is a component diagram, which means a user who plugged
    /// this renderer into <c>PlantUmlRendering.Local</c> without Graphviz installed got that card in
    /// place of their overview and nothing anywhere said why.</para>
    ///
    /// <para><b>Why it is conditional.</b> A machine that HAS Graphviz renders exactly the bytes it
    /// rendered before — the pragma is never added, so no existing output moves. On a machine without it
    /// the pragma is added to every diagram rather than only the ones that need a graph layout, because
    /// nothing here parses the source to find out which those are. Measured: for a sequence and an
    /// activity diagram, which never touch Graphviz, the drawn image is byte-identical with and without
    /// the pragma — the only difference is PlantUML's trailing <c>SRC=[…]</c> comment, which echoes the
    /// source and so honestly reflects the extra line.</para>
    /// </summary>
    internal static string WithLayoutEngine(string plantUml) => WithLayoutEngine(plantUml, DotIsUsable.Value);

    /// <summary>
    /// <see cref="WithLayoutEngine(string)"/> with the Graphviz verdict supplied rather than probed, so
    /// the rewrite can be exercised on a machine that HAS Graphviz — which is most of them, and every one
    /// this repository is normally developed on. Without this seam the only way to test the fallback
    /// would be to restate it in the test, which tests the test.
    /// </summary>
    internal static string WithLayoutEngine(string plantUml, bool dotIsUsable)
    {
        if (dotIsUsable || string.IsNullOrEmpty(plantUml))
            return plantUml;

        // After the @start line, which is where a pragma has to sit. A source without one is not
        // something to guess at, so it goes through untouched.
        var start = plantUml.IndexOf("@start", StringComparison.Ordinal);
        if (start < 0)
            return plantUml;

        var endOfLine = plantUml.IndexOf('\n', start);
        if (endOfLine < 0)
            return plantUml + "\n!pragma layout smetana";

        return plantUml[..(endOfLine + 1)] + "!pragma layout smetana\n" + plantUml[(endOfLine + 1)..];
    }
}
