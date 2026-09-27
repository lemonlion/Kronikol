// Writes split-page.html: a page carrying the shipped render script, for the split-*.js probes, which call its pure
// source functions (window._splitDiagramSource, window._countArrows). Build src/Kronikol first.
// Usage (from this directory): dotnet fsi split-page.fsx split-page.html
#r "../../src/Kronikol/bin/Debug/net10.0/Kronikol.dll"
let script = Kronikol.Reports.DiagramContextMenu.GetPlantUmlBrowserRenderScript()
System.IO.File.WriteAllText(fsi.CommandLineArgs.[1], "<!doctype html><html><head><meta charset=\"utf-8\"></head><body>" + script + "</body></html>")
printfn "wrote %d chars" script.Length
