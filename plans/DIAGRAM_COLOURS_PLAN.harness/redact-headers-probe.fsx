// DIAGRAM_COLOURS_PLAN harness (audit, 3.30.2): the wiki's Bearer recipe as a post-processor and as a
// mid-processor, on a request whose Authorization header carries a 700-character token, through the
// Kronikol.dll named on the command line. Prints how many of the token's characters survive in the note
// source: the header value is cut into 80-character chunks before a post-processor sees it, and a
// mid-processor is never given the headers.
// Usage: dotnet fsi plans/DIAGRAM_COLOURS_PLAN.harness/redact-headers-probe.fsx <path to Kronikol.dll>
open System
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
// The recipe the wiki gives from 3.30.2 for a post-processor: it follows a header value across the lines it is cut into
// (a <U+200B> at the end of each, the header tag at the start of the next) and through the code points.
let spanning = Func<string, string>(fun (c: string) -> Regex.Replace(c, @"(?<=Bearer )(?:[A-Za-z0-9._~+/=-]|<U\+[0-9A-F]{4,6}>(?:\r?\n<color:[^>]+>)?)+", "***"))

// A deterministic 700-character base64url token; the tail marker lets us see what survives.
let rnd = Random(7)
let alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"
let token (withTilde: bool) =
    let body = String(Array.init 690 (fun _ -> alphabet.[rnd.Next alphabet.Length]))
    (if withTilde then body.Substring(0, 30) + "~" + body.Substring(31) else body) + "TAILMARKER"

let make (auth: string) =
    let ctor = logType.GetConstructors() |> Array.maxBy (fun c -> c.GetParameters().Length)
    let ps = ctor.GetParameters()
    let trace = Guid.NewGuid()
    let rr = Guid.NewGuid()
    let arg (p: ParameterInfo) (kind: string) : obj =
        match p.Name with
        | "TestName" | "testName" | "TestId" | "testId" -> box "t"
        | "Method" | "method" -> p.ParameterType.GetMethod("op_Implicit", [| typeof<HttpMethod> |]).Invoke(null, [| box HttpMethod.Get |])
        | "Content" | "content" -> box (if kind = "req" then "" else "{\"ok\":true}")
        | "Uri" | "uri" -> box (Uri("http://h/api/me"))
        | "Headers" | "headers" -> box (if kind = "req" then [| struct ("Authorization", auth) |] else [||])
        | "ServiceName" | "serviceName" -> box "Svc"
        | "CallerName" | "callerName" -> box "Caller"
        | "Type" | "type" -> Enum.Parse(rrType, (if kind = "req" then "Request" else "Response"))
        | "TraceId" | "traceId" -> box trace
        | "RequestResponseId" | "requestResponseId" -> box rr
        | "TrackingIgnore" | "trackingIgnore" -> box false
        | _ -> if p.HasDefaultValue then p.DefaultValue else null
    let arr = Array.CreateInstance(logType, 2)
    arr.SetValue(ctor.Invoke(ps |> Array.map (fun p -> arg p "req")), 0)
    arr.SetValue(ctor.Invoke(ps |> Array.map (fun p -> arg p "resp")), 1)
    arr

for (label, processorParam, processor) in [ "post-processor", "requestPostFormattingProcessor", recipe; "mid-processor", "requestMidFormattingProcessor", recipe; "post, spanning", "requestPostFormattingProcessor", spanning ] do
    for withTilde in [ false; true ] do
        let t = token withTilde
        let ps = get.GetParameters()
        let args = ps |> Array.map (fun p ->
            if p.Name = processorParam then box processor
            elif p.Position = 0 then box (make ("Bearer " + t))
            elif p.HasDefaultValue then p.DefaultValue else null)
        let results = get.Invoke(null, args) :?> System.Collections.IEnumerable
        for r in results do
            let plantUmls = r.GetType().GetProperty("PlantUmls").GetValue(r) :?> System.Collections.IEnumerable
            let first = Seq.head (Seq.cast<obj> plantUmls)
            let plain = first.GetType().GetField("Item1").GetValue(first) :?> string
            // Whatever of the token is left: every header line of the Authorization block, tags and markers removed.
            let lines = plain.Replace("\r", "").Split('\n') |> Array.filter (fun l -> l.Contains("686868>") || l.Contains("color:gray>"))
            let left = lines |> Array.map (fun l -> Regex.Replace(l, @"<color:[^>]+>|<U\+200B>|\[Authorization=|\]", "")) |> String.concat ""
            let survived = left.Replace("Bearer ***", "").Replace("Bearer ", "").Trim()
            printfn "%-15s tilde=%-5b header lines=%2d  token chars left=%3d  tail visible=%b" label withTilde lines.Length survived.Length (left.Contains "TAILMARKER")
