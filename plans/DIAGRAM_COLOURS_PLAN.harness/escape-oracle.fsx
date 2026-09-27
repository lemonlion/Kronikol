// Second P3 audit: runs Kronikol's real escapers over captured text, so the fuzz probe renders exactly what a
// report would carry. Input: a JSON array of { id, kind, raw }. Output: the same with "escaped" added.
//   body   PlantUmlCreator.EscapeCreoleMarkup over "BEFORE\n<raw>\nAFTER" (a payload note's lines)
//   label  PlantUmlCreator.EscapeCapturedLabel over "BEFORE <raw> AFTER" (a request label)
//   doc    StepBarPlantUml.Build("Given x", null, "BEFORE\n<raw>\nAFTER") (a step bar's doc string)
//   cell   StepBarPlantUml.Build("Given x", [ header BEFORE|H|AFTER, row BEFORE|<raw>|AFTER ], null)
//   header PlantUmlCreator's header line for a header whose value is <raw> (through FormatNoteContent)
// The third audit's contexts, the one-line statements and the note the second audit did not draw:
//   step   StepBarPlantUml.Build("BEFORE <raw> AFTER") (a step's own text: a LightBDD or Gherkin parameter value)
//   test   the test delimiter InsertTestDelimiter writes for a test named "BEFORE <raw> AFTER"
//   action a UI action label as PlantUmlCreator writes it (line breaks as \n, then the width wrap)
//   assert DiagramWidth.WrapBlockNoteBody over "BEFORE\n<raw>\nAFTER" (an assertion note's lines)
//   span   an internal-flow activity label (InternalFlowRenderer's escaper and width wrap) for "BEFORE <raw> AFTER"
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
let escapeLoader = creator.GetMethod("EscapeLoaderMarkup", flags)
let widthType = asm.GetType("Kronikol.PlantUml.DiagramWidth")
let wrap = widthType.GetMethod("Wrap", flags)
let wrapBlock = widthType.GetMethod("WrapBlockNoteBody", flags)
let flowType = asm.GetType("Kronikol.InternalFlow.InternalFlowRenderer")
let escapeSpan = flowType.GetMethod("EscapePlantUml", flags)
let wrapSpan = flowType.GetMethod("WrapForWidth", flags)

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
            | "step" -> call build [| box ("BEFORE " + raw + " AFTER"); null; null |]
            | "test" ->
                "hnote across #black:<color:white>Test " + call escapeLoader [| box (("BEFORE " + raw + " AFTER").ReplaceLineEndings(" ")) |]
            | "action" ->
                let label = call escapeLoader [| box (("BEFORE " + raw + " AFTER").Replace("\r", "").Replace("\n", "\\n")) |]
                call wrap [| box label; box 100; box "\\n" |]
            | "assert" -> call wrapBlock [| box ("BEFORE\n" + raw + "\nAFTER") |]
            | "span" -> call wrapSpan [| box (call escapeSpan [| box ("BEFORE " + raw + " AFTER") |]) |]
            | k -> failwithf "unknown kind %s" k
        with e -> "!!EXCEPTION " + (match e.InnerException with null -> e.Message | ie -> ie.Message)
    let d = Collections.Generic.Dictionary<string, obj>()
    d.["id"] <- box id
    d.["codes"] <- box [| for ch in escaped -> int ch |]
    out.Add d
File.WriteAllText(args.[3], JsonSerializer.Serialize(out))
printfn "escaped %d cases" out.Count
