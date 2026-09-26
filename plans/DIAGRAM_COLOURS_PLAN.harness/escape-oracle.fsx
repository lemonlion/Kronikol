// Second P3 audit: runs Kronikol's real escapers over captured text, so the fuzz probe renders exactly what a
// report would carry. Input: a JSON array of { id, kind, raw }. Output: the same with "escaped" added.
//   body   PlantUmlCreator.EscapeCreoleMarkup over "BEFORE\n<raw>\nAFTER" (a payload note's lines)
//   label  PlantUmlCreator.EscapeCapturedLabel over "BEFORE <raw> AFTER" (a request label)
//   doc    StepBarPlantUml.Build("Given x", null, "BEFORE\n<raw>\nAFTER") (a step bar's doc string)
//   cell   StepBarPlantUml.Build("Given x", [ header BEFORE|H|AFTER, row BEFORE|<raw>|AFTER ], null)
//   header PlantUmlCreator's header line for a header whose value is <raw> (through FormatNoteContent)
// Usage: dotnet fsi escape-oracle.fsx <Kronikol.dll> <cases.json> <out.json>
open System
open System.IO
open System.Reflection
open System.Text.Json

let args = fsi.CommandLineArgs
let asm = Assembly.LoadFrom(args.[1])
let flags = BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic
let creator = asm.GetType("Kronikol.PlantUml.PlantUmlCreator")
let escapeBody = creator.GetMethod("EscapeCreoleMarkup", flags)
let escapeLabel = creator.GetMethod("EscapeCapturedLabel", flags)
let barType = asm.GetType("Kronikol.PlantUml.StepBarPlantUml")
let build = barType.GetMethod("Build", flags)
let tableType = asm.GetType("Kronikol.PlantUml.StepBarTable")

let tables (rows: string[][]) =
    let listType = typedefof<System.Collections.Generic.List<_>>.MakeGenericType([| tableType |])
    let list = Activator.CreateInstance(listType)
    listType.GetMethod("Add").Invoke(list, [| Activator.CreateInstance(tableType, [| box null; box rows |]) |]) |> ignore
    list

let call (m: MethodInfo) (a: obj[]) = m.Invoke(null, a) :?> string

let cases = JsonDocument.Parse(File.ReadAllText(args.[2])).RootElement
let out = ResizeArray<Collections.Generic.Dictionary<string, obj>>()
for c in cases.EnumerateArray() do
    let id = c.GetProperty("id").GetString()
    let kind = c.GetProperty("kind").GetString()
    let raw = String([| for u in c.GetProperty("codes").EnumerateArray() -> char (u.GetInt32()) |])
    let escaped =
        try
            match kind with
            | "body" -> call escapeBody [| box ("BEFORE\n" + raw + "\nAFTER") |]
            | "label" -> call escapeLabel [| box ("BEFORE " + raw + " AFTER") |]
            | "doc" -> call build [| box "Given x"; null; box ("BEFORE\n" + raw + "\nAFTER") |]
            | "cell" -> call build [| box "Given x"; tables [| [| "BEFORE"; "H"; "AFTER" |]; [| "BEFORE"; raw; "AFTER" |] |]; null |]
            | k -> failwithf "unknown kind %s" k
        with e -> "!!EXCEPTION " + (match e.InnerException with null -> e.Message | ie -> ie.Message)
    let d = Collections.Generic.Dictionary<string, obj>()
    d.["id"] <- box id
    d.["codes"] <- box [| for ch in escaped -> int ch |]
    out.Add d
File.WriteAllText(args.[3], JsonSerializer.Serialize(out))
printfn "escaped %d cases" out.Count
