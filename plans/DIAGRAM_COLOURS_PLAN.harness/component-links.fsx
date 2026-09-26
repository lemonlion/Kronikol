// Second P3 audit: the component diagram's link form as the public API writes it (GeneratePlantUml with stats,
// the plain palette the JS engines use), so link-rest.js can read which ink each link would rest in.
// Usage: dotnet fsi component-links.fsx <out.puml>   (Kronikol.dll from the audit worktree's Debug build)
#r "../../src/Kronikol/bin/Debug/net10.0/Kronikol.dll"
open System.Collections.Generic
open Kronikol.ComponentDiagram

let stats calls = RelationshipStats(calls, 2, 10.0, 12.0, 30.0, 45.0, 1.0, 50.0, 0.0, Dictionary(), [||], null, null, false, 0.1, Dictionary(), null, 5.0)
let rel caller service protocol (methods: string list) category =
    ComponentRelationship(caller, service, protocol, HashSet(methods), 3, 2, category)
let rels =
    [| rel "Caller" "Orders API" "HTTP" [ "GET"; "POST" ] "HTTP"
       rel "Orders API" "Orders DB" "SQL" [ "SELECT" ] "SQL"
       rel "Orders API" "Events" "ServiceBus" [ "Send" ] "ServiceBus" |]
let key (r: ComponentRelationship) = "iflow-rel-" + r.Caller.Replace(" ", "_").Replace("/", "_") + "-" + r.Service.Replace(" ", "_").Replace("/", "_")
let statsMap = Dictionary<string, RelationshipStats>()
for r in rels do statsMap.[key r] <- stats 3
let puml = ComponentDiagramGenerator.GeneratePlantUml(rels, ComponentDiagramOptions(), statsMap, false)
System.IO.File.WriteAllText(fsi.CommandLineArgs.[1], puml)
printfn "%s" puml
