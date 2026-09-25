// DIAGRAM_COLOURS_PLAN harness, I3: a mergeable TestRunReport.json written by a given Kronikol.dll through the
// real pipeline (RequestResponseLogger, then ReportGenerator.CreateStandardReportsWithDiagrams with
// GenerateMergeableData), so the header lines in it are whatever that build's emitter writes.
//   <label>-post        POST with request and response headers and JSON bodies (the YAML toggle, copy text)
//   <label>-headersonly GET whose request note is headers only (hidden headers leave that note empty)
//   <label>-longpath    GET with a 2,300-character query, so the note carries the [Full path] block
// A third argument turns an option off:
//   plain   SequenceDiagramArrowColors: the context menu's "Copy all caller request payloads" matches only a
//           plain `caller ->` arrow, so it is offered on no diagram written with the default.
//   noflow  InternalFlowTracking: a truncated label is capped at 2,000 characters with the [[#iflow-…]] link
//           around it, which the browser worker cannot draw (F25), so [Full path] is seen only without it.
// Usage (from the repository root; the reference is the build under test):
//   dotnet fsi -r:<build>/Kronikol.dll plans/DIAGRAM_COLOURS_PLAN.harness/emit-merge-inputs.fsx <label> <out dir> [plain|noflow]
open System
open System.IO
open System.Net
open System.Net.Http
open Kronikol
open Kronikol.Tracking
open Kronikol.Reports

let label = fsi.CommandLineArgs.[1]
let outDir = Path.GetFullPath fsi.CommandLineArgs.[2]
let flag name = fsi.CommandLineArgs.Length > 3 && fsi.CommandLineArgs.[3] = name
let plainArrows = flag "plain"
let noFlow = flag "noflow"

let headers (pairs: (string * string) list) = pairs |> List.map (fun (k, v) -> struct (k, v)) |> Array.ofList

let call (testId: string) (verb: HttpMethod) (uri: string) reqHeaders (reqBody: string) (status: HttpStatusCode) respHeaders (respBody: string) =
    let trace = Guid.NewGuid()
    let rr = Guid.NewGuid()
    let m = OneOf<HttpMethod, string>.op_Implicit(verb)
    RequestResponseLogger.Log(RequestResponseLog(testId, testId, m, reqBody, Uri(uri), headers reqHeaders, "Orders API", "Caller", RequestResponseType.Request, trace, rr, false))
    RequestResponseLogger.Log(RequestResponseLog(testId, testId, m, respBody, Uri(uri), headers respHeaders, "Orders API", "Caller", RequestResponseType.Response, trace, rr, false, StatusCode = OneOf<HttpStatusCode, string>.op_Implicit(status)))

let id suffix = sprintf "i3-%s-%s" label suffix
let json = ("Content-Type", "application/json")

call (id "post") HttpMethod.Post "http://orders-api/api/orders"
    [ ("Accept", "application/json"); ("X-Correlation-Id", "corr-" + label); json ]
    "{\"sku\":\"A1\",\"quantity\":2,\"customer\":{\"id\":\"C-9\",\"tier\":\"gold\"}}"
    HttpStatusCode.Created [ ("Location", "/api/orders/42"); json ]
    "{\"id\":42,\"status\":\"created\",\"items\":[{\"sku\":\"A1\",\"quantity\":2}]}"

call (id "headersonly") HttpMethod.Get "http://orders-api/api/orders/42"
    [ ("Accept", "application/json"); ("X-Correlation-Id", "corr-" + label) ] ""
    HttpStatusCode.OK [ json ] "{\"id\":42,\"status\":\"created\"}"

let longQuery = String.Join("&", [ for i in 1 .. 115 -> sprintf "filter%03d=value%03d" i i ])
call (id "longpath") HttpMethod.Get ("http://orders-api/api/orders?" + longQuery)
    [ ("Accept", "application/json") ] ""
    HttpStatusCode.OK [ json ] "{\"count\":0}"

let scenario suffix name = Scenario(Id = id suffix, DisplayName = name, Result = ExecutionResult.Passed, IsHappyPath = true, Duration = TimeSpan.FromSeconds 1.0)
let features =
    [| Feature(DisplayName = sprintf "Written by the %s build" label,
               Scenarios = [| scenario "post" (label + ": POST with headers and JSON bodies")
                              scenario "headersonly" (label + ": GET whose request note is headers only")
                              scenario "longpath" (label + ": GET with a full-path block") |]) |]

let start = DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc)
let options = ReportConfigurationOptions(ReportsFolderPath = outDir, GenerateMergeableData = true, SpecificationsTitle = "I3 " + label, WriteHistoryLedger = Nullable false, SequenceDiagramArrowColors = not plainArrows, InternalFlowTracking = not noFlow)
ReportGenerator.CreateStandardReportsWithDiagrams(features, start, start.AddMinutes 1.0, options)

let report = Path.Combine(outDir, "TestRunReport.json")
let text = File.ReadAllText report
// The JSON writer escapes `<` as <, so the tags are counted without it.
printfn "%s: %s, %d bytes, color:gray x%d, color:# x%d, [Full path] x%d"
    label report text.Length
    ((text.Length - text.Replace("color:gray", "").Length) / "color:gray".Length)
    ((text.Length - text.Replace("color:#", "").Length) / "color:#".Length)
    ((text.Length - text.Replace("[Full path]", "").Length) / "[Full path]".Length)
