// DIAGRAM_COLOURS_PLAN harness (audit, 3.30.2): the wiki's Bearer redaction recipe as a request
// mid-processor, over bodies whose token carries a tilde or a doubled marker, through the Kronikol.dll named
// on the command line (a build, or a published package's lib/net10.0/Kronikol.dll). Prints what is left of
// each token in the note source: LEAKS means a part of the token is still there.
// Usage: dotnet fsi plans/DIAGRAM_COLOURS_PLAN.harness/redact-probe.fsx <path to Kronikol.dll>
open System
open System.IO
open System.Net
open System.Net.Http
open System.Reflection
open System.Text.RegularExpressions

let dll = fsi.CommandLineArgs.[1]
let asm = Assembly.LoadFrom dll
let creator = asm.GetType("Kronikol.PlantUml.PlantUmlCreator")
let logType = asm.GetType("Kronikol.Tracking.RequestResponseLog")
let rrType = asm.GetType("Kronikol.Tracking.RequestResponseType")
let get = creator.GetMethod("GetPlantUmlImageTagsPerTestId")

let recipe = Func<string, string>(fun (c: string) -> Regex.Replace(c, @"Bearer [A-Za-z0-9\-._~+/]+=*", "Bearer ***"))

let make (body: string) =
    // Build the two logs through the constructor the tests use; parameters by position.
    let ctor = logType.GetConstructors() |> Array.maxBy (fun c -> c.GetParameters().Length)
    let ps = ctor.GetParameters()
    let trace = Guid.NewGuid()
    let rr = Guid.NewGuid()
    let arg (p: ParameterInfo) (kind: string) : obj =
        match p.Name with
        | "TestName" | "testName" -> box "t"
        | "TestId" | "testId" -> box "t"
        | "Method" | "method" -> p.ParameterType.GetMethod("op_Implicit", [| typeof<HttpMethod> |]).Invoke(null, [| box HttpMethod.Post |])
        | "Content" | "content" -> box (if kind = "req" then body else "{\"ok\":true}")
        | "Uri" | "uri" -> box (Uri("http://h/api/echo"))
        | "Headers" | "headers" -> box [| struct ("Content-Type", "application/json") |]
        | "ServiceName" | "serviceName" -> box "Svc"
        | "CallerName" | "callerName" -> box "Caller"
        | "Type" | "type" -> Enum.Parse(rrType, (if kind = "req" then "Request" else "Response"))
        | "TraceId" | "traceId" -> box trace
        | "RequestResponseId" | "requestResponseId" -> box rr
        | "TrackingIgnore" | "trackingIgnore" -> box false
        | _ -> if p.HasDefaultValue then p.DefaultValue else null
    let req = ctor.Invoke(ps |> Array.map (fun p -> arg p "req"))
    let resp = ctor.Invoke(ps |> Array.map (fun p -> arg p "resp"))
    let arr = Array.CreateInstance(logType, 2)
    arr.SetValue(req, 0); arr.SetValue(resp, 1)
    arr

let tokens = [
    "tilde", "Bearer abc~def~ghi"
    "two dashes pairs", "Bearer eyJhb--GciOi--JIUzI1"
    "two underscore pairs", "Bearer eyJhb__GciOi__JIUzI1"
    "two slash pairs", "Bearer ab//cdEF//gh+="
    "plain control", "Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.sig-_x"
]
for (name, token) in tokens do
    let body = sprintf "{\"echo\":\"%s\"}" token
    let ps = get.GetParameters()
    let args = ps |> Array.map (fun p ->
        match p.Name with
        | "requestMidFormattingProcessor" -> box recipe
        | n when p.Position = 0 -> box (make body)
        | _ -> if p.HasDefaultValue then p.DefaultValue else null)
    let results = get.Invoke(null, args) :?> System.Collections.IEnumerable
    for r in results do
        let plantUmls = r.GetType().GetProperty("PlantUmls").GetValue(r) :?> System.Collections.IEnumerable
        let first = Seq.head (Seq.cast<obj> plantUmls)
        let plain = first.GetType().GetField("Item1").GetValue(first) :?> string
        let line = plain.Split('\n') |> Array.tryFind (fun l -> l.Contains("\"echo\""))
        let leftover = match line with Some l -> l.Trim() | None -> "(no echo line)"
        let leaked = leftover.Contains("***") && Regex.IsMatch(leftover, @"\*\*\*[^""]")
        printfn "%-22s %-8s %s" name (if leaked then "LEAKS" else "redacted") leftover
