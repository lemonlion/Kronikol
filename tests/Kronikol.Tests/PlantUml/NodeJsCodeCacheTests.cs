using Kronikol.PlantUml;

namespace Kronikol.Tests.PlantUml;

/// <summary>
/// The engine's V8 code cache, watched from outside. The fact deletes and then corrupts the machine's one cache file,
/// so it runs in <see cref="NodeCodeCacheCollection"/>, after every other render in the assembly: a parallel render
/// that rebuilt the file between the delete and the fact's own render turned its "miss" into "hit" on CI.
/// </summary>
[Collection(NodeCodeCacheCollection.Name)]
public class NodeJsCodeCacheTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void Code_cache_is_created_on_first_run_reused_afterwards_and_regenerated_when_v8_rejects_it()
    {
        Assert.SkipWhen(!NodeJsPlantUmlRendererTests.IsNodeAvailable(), "Node.js not available on PATH");

        var cachePath = NodeJsPlantUmlRenderer.CodeCachePath;
        NodeJsPlantUmlRenderer.Render(Seq("Warm", "Up"), PlantUmlImageFormat.Svg); // makes sure the engine is downloaded
        if (File.Exists(cachePath)) File.Delete(cachePath);

        NodeJsPlantUmlRenderer.Render(Seq("Cold", "One"), PlantUmlImageFormat.Svg);
        Assert.True(File.Exists(cachePath), "code cache should be written on the first run");
        Assert.Equal("miss", NodeJsPlantUmlRenderer.LastCodeCacheStatus);
        var written = new FileInfo(cachePath).Length;
        Assert.True(written > 1024, $"code cache is suspiciously small: {written} bytes");

        NodeJsPlantUmlRenderer.Render(Seq("Warm", "Two"), PlantUmlImageFormat.Svg);
        Assert.Equal("hit", NodeJsPlantUmlRenderer.LastCodeCacheStatus);

        // A cache that fails its checksum (here: garbage) or that V8 refuses (a node upgrade) is rebuilt, and the
        // render still works.
        File.WriteAllBytes(cachePath, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var svg = System.Text.Encoding.UTF8.GetString(NodeJsPlantUmlRenderer.Render(Seq("Rejected", "Three"), PlantUmlImageFormat.Svg));
        Assert.Contains("<svg", svg);
        Assert.Equal("rejected", NodeJsPlantUmlRenderer.LastCodeCacheStatus);
        Assert.True(new FileInfo(cachePath).Length > 1024, "a rejected code cache should be regenerated");

        var batch = NodeJsPlantUmlRenderer.RenderMany([Seq("Batch", "Four")]);
        Assert.True(batch[0].Succeeded, batch[0].Error);
        Assert.Equal("hit", NodeJsPlantUmlRenderer.LastCodeCacheStatus);
    }

    private static string Seq(string a, string b) => NodeJsPlantUmlRendererTests.Seq(a, b);
}
