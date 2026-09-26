// Second P3 audit: what the payload escaper costs on a large body, per MB of captured text.
#r "../../src/Kronikol/bin/Debug/net10.0/Kronikol.dll"
open System
open System.Reflection
let m = typeof<Kronikol.PlantUml.PlantUmlCreator>.GetMethod("EscapeCreoleMarkup", BindingFlags.Static ||| BindingFlags.NonPublic)
let esc (s: string) = m.Invoke(null, [| box s |]) :?> string
let line i = sprintf "  \"field%d\": \"value %d with some text, a url https://example.com/a/b?c=%d&d=e and ~tilde %%date() **x**\"," i i i
let body = String.Join("\n", [| for i in 1 .. 100000 -> line i |])
printfn "body %.1f MB" (float body.Length / 1048576.0)
esc body |> ignore
let sw = Diagnostics.Stopwatch.StartNew()
for _ in 1 .. 3 do esc body |> ignore
printfn "EscapeCreoleMarkup: %.0f ms per MB (Debug build)" (float sw.ElapsedMilliseconds / 3.0 / (float body.Length / 1048576.0))
