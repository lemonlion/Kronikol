namespace Kronikol.Tests.PlantUml;

/// <summary>
/// Tests that delete or overwrite the engine's V8 code cache run here, after every parallel collection.
/// </summary>
/// <remarks>
/// The cache is one file per machine (<see cref="Kronikol.PlantUml.NodeJsPlantUmlRenderer.CodeCachePath"/>), and every
/// class that renders through Node reads it and writes it back when it is missing or rejected. A test that deletes or
/// corrupts it to watch the renderer rebuild it can have another class's render rebuild it first: on CI the code-cache
/// fact deleted the file, a render elsewhere wrote it back, and the fact's own first render read "hit" where it wanted
/// "miss". <c>DisableParallelization</c> runs this collection after every parallel one, so nothing else renders while
/// its tests run. <see cref="SharedCodeCacheTests"/> keeps every such test here; its only member is
/// <see cref="NodeJsCodeCacheTests"/>.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NodeCodeCacheCollection
{
    public const string Name = "NodeCodeCache";
}
