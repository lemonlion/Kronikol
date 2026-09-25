// DIAGRAM_COLOURS_PLAN harness (third pass): the PlantUML source Kronikol's own emitter writes for three
// captured calls, so the loader probe renders the bytes a report carries rather than hand-written ones.
//   payload-rust.puml     a 400 whose JSON body quotes a Rust type, Vec<&str>  (the OpenIconic syntax <&name>)
//   payload-emoji.puml    a request whose JSON body carries <:rocket:>          (the emoji syntax <:name:>)
//   payload-control.puml  the same call with a plain body
//   payload-stepbar-lightbdd.puml  a step bar as StepCollector builds it for a LightBDD step with a table
//                         parameter, whose name LightBDD formats as `I have data [inputs: "<$inputs>"]`
//   payload-stepbar-rust.puml      a step bar whose step text quotes Vec<&str> (a parameter value inline)
//   payload-amp.puml      a query string and a JSON body carrying `&` (well-formedness of the Node SVG)
//   payload-xml.puml      XML request and response bodies (`<` in painted text)
// Usage (from the repo root, after any build of tests/Kronikol.Tests):
//   dotnet fsi plans/DIAGRAM_COLOURS_PLAN.harness/emit-payload-sources.fsx
#r "../../tests/Kronikol.Tests/bin/Debug/net10.0/Kronikol.dll"
open System
open System.IO
open System.Net
open System.Net.Http
open System.Reflection
open Kronikol.Tracking
open Kronikol.PlantUml

let here = __SOURCE_DIRECTORY__

// StepBarPlantUml is internal: call its Build the way StepCollector does (StepCollector.cs:99).
let private asm = typeof<PlantUmlCreator>.Assembly
let private tableType = asm.GetType("Kronikol.PlantUml.StepBarTable")
let private build = asm.GetType("Kronikol.PlantUml.StepBarPlantUml").GetMethod("Build", BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic)
let stepBar (label: string) (rows: string[][] option) =
    let tables =
        match rows with
        | None -> null
        | Some r ->
            let listType = typedefof<System.Collections.Generic.List<_>>.MakeGenericType([| tableType |])
            let list = Activator.CreateInstance(listType)
            listType.GetMethod("Add").Invoke(list, [| Activator.CreateInstance(tableType, [| box null; box r |]) |]) |> ignore
            list
    build.Invoke(null, [| box label; tables; null |]) :?> string

// The two marker logs TrackingDiagramOverride.InsertPlantUml writes (StartOverride + EndOverride).
let marker (testId: string) (bar: string) =
    let log () = RequestResponseLog(testId, testId, OneOf<HttpMethod, string>.op_Implicit(""), "", Uri("http://override.com"), [||], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
    let start = log ()
    start.IsOverrideStart <- true
    start.MarkerKind <- DiagramMarkerKind.Step
    start.PlantUml <- "\n" + bar + "\n\n\n"
    let stop = log ()
    stop.IsOverrideEnd <- true
    stop.MarkerKind <- DiagramMarkerKind.Step
    [ start; stop ]

let pairAt (uriText: string) (contentType: string) (testId: string) (reqBody: string) (respBody: string) (status: HttpStatusCode) =
    let trace = Guid.NewGuid()
    let rr = Guid.NewGuid()
    let uri = Uri(uriText)
    let json = [| struct ("Content-Type", contentType) |]
    let post = OneOf<HttpMethod, string>.op_Implicit(HttpMethod.Post)
    let req = RequestResponseLog(testId, testId, post, reqBody, uri, json, "Orders API", "Caller", RequestResponseType.Request, trace, rr, false)
    let resp = RequestResponseLog(testId, testId, post, respBody, uri, json, "Orders API", "Caller", RequestResponseType.Response, trace, rr, false, StatusCode = OneOf<HttpStatusCode, string>.op_Implicit(status))
    [ req; resp ]
let pair = pairAt "http://orders-api/api/orders" "application/json"

let logs =
    List.concat [
        pair "payload-rust" "{\"sku\":\"A1\"}" "{\"error\":\"expected Vec<&str>, found String\"}" HttpStatusCode.BadRequest
        pair "payload-emoji" "{\"text\":\"shipped <:rocket:> today\"}" "{\"ok\":true}" HttpStatusCode.OK
        pair "payload-control" "{\"sku\":\"A1\"}" "{\"ok\":true}" HttpStatusCode.OK
        marker "payload-stepbar-lightbdd" (stepBar "Given I have data [inputs: \"<$inputs>\"]" (Some [| [| "Id"; "Name" |]; [| "1"; "Ann" |] |]))
        pair "payload-stepbar-lightbdd" "{\"sku\":\"A1\"}" "{\"ok\":true}" HttpStatusCode.OK
        marker "payload-stepbar-rust" (stepBar "Then the error mentions \"Vec<&str>\"" None)
        pair "payload-stepbar-rust" "{\"sku\":\"A1\"}" "{\"ok\":true}" HttpStatusCode.OK
        pairAt "http://orders-api/api/orders?page=1&size=10" "application/json" "payload-amp" "{\"sku\":\"A1\"}" "{\"note\":\"fish & chips\"}" HttpStatusCode.OK
        pairAt "http://orders-api/api/orders" "application/xml" "payload-xml" "<order><sku>A1</sku></order>" "<result><ok>true</ok></result>" HttpStatusCode.OK
    ]

for r in PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs) do
    let struct (plain, _) = Seq.head r.PlantUmls
    let file = Path.Combine(here, r.TestId + ".puml")
    File.WriteAllText(file, plain.Replace("\r\n", "\n"))
    printfn "== %s (%d bytes)" (Path.GetFileName file) plain.Length
    printfn "%s" plain
