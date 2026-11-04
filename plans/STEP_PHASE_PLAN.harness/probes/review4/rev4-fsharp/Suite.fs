namespace Kronikol.Tracking

open System

[<AttributeUsage(AttributeTargets.Assembly)>]
type internal TrackStepsAttribute() = inherit Attribute()

[<AttributeUsage(AttributeTargets.Method)>]
type internal GivenStepAttribute() =
    inherit Attribute()
    member val Description : string = null with get, set
    member val SkipIf : string = null with get, set
    member val SkipReason : string = null with get, set

namespace Rev4.FSharp

open System
open System.IO
open System.Threading.Tasks
open System.Runtime.CompilerServices
open Kronikol.Tracking
open Kronikol.xUnit3
open Xunit

[<assembly: TrackSteps>]
[<assembly: CollectionBehavior(DisableTestParallelization = true)>]
do ()

module Helpers =
    let out = System.Collections.Generic.List<string>()
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let taskHelper (label: string) : Task = Task.Delay(1)
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let taskOfIntHelper (label: string) : Task<int> = Task.FromResult(1)
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let throwingTaskHelper (label: string) : Task = raise (InvalidOperationException(label))
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let unitHelper (label: string) : unit = out.Add(label)
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let throwingUnitHelper (label: string) : unit = raise (InvalidOperationException(label))

type Shapes() =
    inherit DiagrammedComponentTest()

    [<GivenStep; MethodImpl(MethodImplOptions.NoInlining)>]
    member _.Given_task_tail() : Task = Helpers.taskHelper "a"

    [<GivenStep; MethodImpl(MethodImplOptions.NoInlining)>]
    member _.Given_task_of_int_tail() : Task<int> = Helpers.taskOfIntHelper "a"

    [<GivenStep; MethodImpl(MethodImplOptions.NoInlining)>]
    member _.Given_task_throwing_tail() : Task = Helpers.throwingTaskHelper "boom"

    [<GivenStep; MethodImpl(MethodImplOptions.NoInlining)>]
    member _.Given_unit_tail() : unit = Helpers.unitHelper "a"

    [<GivenStep; MethodImpl(MethodImplOptions.NoInlining)>]
    member _.Given_unit_throwing_tail() : unit = Helpers.throwingUnitHelper "boom"

    [<GivenStep; MethodImpl(MethodImplOptions.NoInlining)>]
    member _.Given_task_fn(f: string -> Task) : Task = f "a"

    [<GivenStep; MethodImpl(MethodImplOptions.NoInlining)>]
    member _.Given_task_fn_throws(f: string -> Task) : Task = f "boom"

    [<GivenStep; MethodImpl(MethodImplOptions.NoInlining)>]
    member _.Given_task_generic<'T>(f: 'T -> Task<'T>, v: 'T) : Task<'T> = f v

    [<GivenStep; MethodImpl(MethodImplOptions.NoInlining)>]
    member _.Given_unit_fn(f: string -> unit) : unit = f "a"

    [<GivenStep; MethodImpl(MethodImplOptions.NoInlining)>]
    member _.Given_task_ce(label: string) : Task =
        task {
            do! Task.Delay(1)
            Helpers.out.Add(label)
        } :> Task

    [<GivenStep; MethodImpl(MethodImplOptions.NoInlining)>]
    member _.Given_task_ce_throws(label: string) : Task =
        task {
            do! Task.Delay(1)
            failwith label
        } :> Task

    [<GivenStep; MethodImpl(MethodImplOptions.NoInlining)>]
    member _.Given_task_ce_int(label: string) : Task<int> =
        task {
            do! Task.Delay(1)
            return label.Length
        }

    [<GivenStep; MethodImpl(MethodImplOptions.NoInlining)>]
    member _.Given_task_ce_try(label: string) : Task =
        task {
            try
                do! Task.Delay(1)
                failwith label
            with _ ->
                Helpers.out.Add("caught")
        } :> Task

    [<GivenStep; MethodImpl(MethodImplOptions.NoInlining)>]
    member _.Given_async_to_task(label: string) : Task =
        let work = async {
            do! Async.Sleep 1
            Helpers.out.Add(label)
        }
        Async.StartAsTask(work) :> Task

    member private _.Report(shape: string, run: unit -> Task) =
        task {
            let id = TestContext.Current.Test.UniqueID
            Helpers.out.Add("resolver=" + string (Track.TestIdResolver <> null) + " same=" + string (Track.TestIdResolver <> null && Track.TestIdResolver.Invoke() = id))
            try
                do! run ()
                Helpers.out.Add(shape + ": no exception")
            with e ->
                Helpers.out.Add(shape + ": " + e.GetType().Name + ": " + e.Message)
            let steps = StepCollector.GetSteps(id) |> Array.map (fun s -> sprintf "%s[%O]" s.Text s.Status)
            let dir = match Environment.GetEnvironmentVariable "PHASE_PROBE_OUT" with | null -> AppContext.BaseDirectory | d -> d
            Directory.CreateDirectory dir |> ignore
            File.AppendAllText(Path.Combine(dir, "rev4-fsharp.txt"), shape + " steps=" + String.Join(",", steps) + " open=" + string (StepCollector.HasActiveStep id) + " :: " + String.Join(" | ", Helpers.out) + "\n")
            Helpers.out.Clear()
        } :> Task

    [<Fact>] member x.T1() : Task = x.Report("task-tail", fun () -> x.Given_task_tail())
    [<Fact>] member x.T2() : Task = x.Report("task-of-int-tail", fun () -> x.Given_task_of_int_tail() :> Task)
    [<Fact>] member x.T3() : Task = x.Report("task-throwing-tail", fun () -> x.Given_task_throwing_tail())
    [<Fact>] member x.T4() : Task = x.Report("unit-tail", fun () -> x.Given_unit_tail(); Task.CompletedTask)
    [<Fact>] member x.T5() : Task = x.Report("unit-throwing-tail", fun () -> x.Given_unit_throwing_tail(); Task.CompletedTask)
    [<Fact>] member x.T6() : Task = x.Report("task-fn", fun () -> x.Given_task_fn(fun _ -> Task.Delay(1)))
    [<Fact>] member x.T7() : Task = x.Report("task-fn-throws", fun () -> x.Given_task_fn_throws(fun s -> raise (InvalidOperationException(s))))
    [<Fact>] member x.T8() : Task = x.Report("task-generic", fun () -> x.Given_task_generic((fun (v:int) -> Task.FromResult(v)), 1) :> Task)
    [<Fact>] member x.T9() : Task = x.Report("unit-fn", fun () -> x.Given_unit_fn(ignore); Task.CompletedTask)
    [<Fact>] member x.T10() : Task = x.Report("task-ce", fun () -> x.Given_task_ce("a"))
    [<Fact>] member x.T11() : Task = x.Report("task-ce-throws", fun () -> x.Given_task_ce_throws("boom"))
    [<Fact>] member x.T12() : Task = x.Report("task-ce-int", fun () -> x.Given_task_ce_int("a") :> Task)
    [<Fact>] member x.T13() : Task = x.Report("task-ce-try", fun () -> x.Given_task_ce_try("boom"))
    [<Fact>] member x.T14() : Task = x.Report("async-to-task", fun () -> x.Given_async_to_task("a"))

