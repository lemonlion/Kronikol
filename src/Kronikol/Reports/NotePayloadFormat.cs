namespace Kronikol.Reports;

/// <summary>
/// The initial display format for JSON note payloads in browser-rendered sequence diagrams
/// (<see cref="PlantUmlRendering.BrowserJs"/> only). Whatever the default, readers can still switch
/// any eligible note — or all of them — between JSON and YAML in the report itself.
/// </summary>
public enum NotePayloadFormat
{
    /// <summary>Note payloads start in their original JSON form (the default).</summary>
    Json,

    /// <summary>Eligible JSON note payloads start in the derived YAML view.</summary>
    Yaml
}
