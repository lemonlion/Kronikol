#:project ../../src/Kronikol.PlantUml.Ikvm/Kronikol.PlantUml.Ikvm.csproj
#:property PublishAot=false
// DIAGRAM_COLOURS_PLAN harness (3.30.1): renders every <id>.puml in a directory to <id>.svg with the IKVM
// package (the Java engine PlantUmlRendering.Local uses), or <id>.err when it throws, so preproc-probe.js
// can read the Java engine's painted text with SVG_IN.
// Usage (from this directory): dotnet run ikvm-render.cs -- <dir>
using Kronikol;

var dir = args[0];
foreach (var file in Directory.GetFiles(dir, "*.puml"))
{
    try { File.WriteAllBytes(Path.ChangeExtension(file, ".svg"), IkvmPlantUmlRenderer.Render(File.ReadAllText(file), PlantUmlImageFormat.Svg)); }
    catch (Exception e) { File.WriteAllText(Path.ChangeExtension(file, ".err"), e.GetType().Name + ": " + e.Message); }
}
Console.WriteLine("rendered " + Directory.GetFiles(dir, "*.puml").Length + " sources with IKVM, layout " + IkvmPlantUmlRenderer.LayoutEngine);
