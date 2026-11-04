import re, sys
path = sys.argv[1]
with open(path, encoding="utf-8", newline="") as f:
    text = f.read()
nl = "\r\n" if "\r\n" in text else "\n"
def block(signature, *body, suffix=""):
    lines = [f"    {signature} ="] + ["        task {"] + [f"            {b}" for b in body] + [f"        }}{suffix}"]
    return nl.join(lines)
replacements = {
    "member _.Given_task_ce(label: string) : Task = task { do! Task.Delay(1); Helpers.out.Add(label) } :> Task":
        block("member _.Given_task_ce(label: string) : Task", "do! Task.Delay(1)", "Helpers.out.Add(label)", suffix=" :> Task"),
    "member _.Given_task_ce_throws(label: string) : Task = task { do! Task.Delay(1); failwith label } :> Task":
        block("member _.Given_task_ce_throws(label: string) : Task", "do! Task.Delay(1)", "failwith label", suffix=" :> Task"),
    "member _.Given_task_ce_int(label: string) : Task<int> = task { do! Task.Delay(1); return label.Length }":
        block("member _.Given_task_ce_int(label: string) : Task<int>", "do! Task.Delay(1)", "return label.Length"),
    "member _.Given_task_ce_try(label: string) : Task = task { try do! Task.Delay(1); failwith label with _ -> Helpers.out.Add(\"caught\") } :> Task":
        block("member _.Given_task_ce_try(label: string) : Task", "try", "    do! Task.Delay(1)", "    failwith label", "with _ ->", "    Helpers.out.Add(\"caught\")", suffix=" :> Task"),
    "member _.Given_async_to_task(label: string) : Task = Async.StartAsTask(async { do! Async.Sleep 1; Helpers.out.Add(label) }) :> Task":
        nl.join([
            "    member _.Given_async_to_task(label: string) : Task =",
            "        let work = async {",
            "            do! Async.Sleep 1",
            "            Helpers.out.Add(label)",
            "        }",
            "        Async.StartAsTask(work) :> Task",
        ]),
}
for old, new in replacements.items():
    full = "    " + old
    if full not in text:
        raise SystemExit("not found: " + old[:60])
    text = text.replace(full, new, 1)
with open(path, "w", encoding="utf-8", newline="") as f:
    f.write(text)
print("fixed")
