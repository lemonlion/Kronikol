import sys
path = sys.argv[1]
with open(path, encoding="utf-8", newline="") as f:
    text = f.read()
nl = "\r\n" if "\r\n" in text else "\n"
members = nl.join([
    "    [<GivenStep; MethodImpl(MethodImplOptions.NoInlining)>]",
    "    member _.Given_task_ce(label: string) : Task = task { do! Task.Delay(1); Helpers.out.Add(label) } :> Task",
    "",
    "    [<GivenStep; MethodImpl(MethodImplOptions.NoInlining)>]",
    "    member _.Given_task_ce_throws(label: string) : Task = task { do! Task.Delay(1); failwith label } :> Task",
    "",
    "    [<GivenStep; MethodImpl(MethodImplOptions.NoInlining)>]",
    "    member _.Given_task_ce_int(label: string) : Task<int> = task { do! Task.Delay(1); return label.Length }",
    "",
    "    [<GivenStep; MethodImpl(MethodImplOptions.NoInlining)>]",
    "    member _.Given_task_ce_try(label: string) : Task = task { try do! Task.Delay(1); failwith label with _ -> Helpers.out.Add(\"caught\") } :> Task",
    "",
    "    [<GivenStep; MethodImpl(MethodImplOptions.NoInlining)>]",
    "    member _.Given_async_to_task(label: string) : Task = Async.StartAsTask(async { do! Async.Sleep 1; Helpers.out.Add(label) }) :> Task",
    "",
    ""
])
marker = "    member private _.Report(shape: string, run: unit -> Task) ="
if "Given_task_ce(" not in text:
    text = text.replace(marker, members + marker, 1)
facts = nl.join([
    "    [<Fact>] member x.T10() : Task = x.Report(\"task-ce\", fun () -> x.Given_task_ce(\"a\"))",
    "    [<Fact>] member x.T11() : Task = x.Report(\"task-ce-throws\", fun () -> x.Given_task_ce_throws(\"boom\"))",
    "    [<Fact>] member x.T12() : Task = x.Report(\"task-ce-int\", fun () -> x.Given_task_ce_int(\"a\") :> Task)",
    "    [<Fact>] member x.T13() : Task = x.Report(\"task-ce-try\", fun () -> x.Given_task_ce_try(\"boom\"))",
    "    [<Fact>] member x.T14() : Task = x.Report(\"async-to-task\", fun () -> x.Given_async_to_task(\"a\"))",
    ""
])
if "T10()" not in text:
    anchor = "    [<Fact>] member x.T9()"
    i = text.index(anchor)
    j = text.index(nl, i) + len(nl)
    text = text[:j] + facts + text[j:]
with open(path, "w", encoding="utf-8", newline="") as f:
    f.write(text)
print("ok")
