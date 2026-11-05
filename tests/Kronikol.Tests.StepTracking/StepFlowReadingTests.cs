using System.Reflection;
using Kronikol.Reports;
using Kronikol.StepTracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.StepTracking;

/// <summary>
/// Which step a call reads when its flow says so, woven and run for real and read back through the report's attribution. The URL of
/// each call says where it was really made, so a fact compares the two.
/// </summary>
[Collection(StepOptionsCollection.Name)]
public class StepFlowReadingTests
{
    private const string Source = """
        using System;
        using System.Net.Http;
        using System.Threading;
        using System.Threading.Tasks;
        using Kronikol.Tracking;

        [assembly: TrackSteps]

        public class Steps
        {
            public string Id;
            public Func<Guid, IDisposable> BeginServiceScope;
            private Task _late;
            private readonly TaskCompletionSource _go = new();
            private readonly TaskCompletionSource _both = new();

            public void Call(string where, string caller = "Tests", string service = "Svc", Guid? trace = null) =>
                RequestResponseLogger.LogPair(Id, Id, HttpMethod.Get, new Uri("http://svc.local/" + where), service, caller);

            private Task Elsewhere(string where)
            {
                using (ExecutionContext.SuppressFlow())
                    return Task.Run(() => RequestResponseLogger.LogPair(Id, Id, HttpMethod.Get, new Uri("http://svc.local/" + where), "Svc", "Svc2"));
            }

            // Two top-level steps at once
            public async Task TopLevelAtOnce() { var a = TopFirst(); var b = TopSecond(); await Task.WhenAll(a, b); Call("after-both"); }
            [Step] async Task TopFirst() { await _go.Task; Call("in-first"); }
            [Step] async Task TopSecond() { await Task.Yield(); Call("in-second"); _go.SetResult(); }

            // Between two steps
            public async Task BetweenSteps() { await One(); Call("between"); await Two(); Call("after-two"); }
            [Step] async Task One() { await Task.Yield(); Call("in-one"); }
            [Step] async Task Two() { await Task.Yield(); Call("in-two"); }

            // A late call reads the step open at the time
            public async Task LateCall() { await Starter(); await Next(); }
            [Step] async Task Starter() { _late = Task.Run(async () => { await _go.Task; Call("detached"); }); await Task.Yield(); Call("in-starter"); }
            [Step] async Task Next() { _go.SetResult(); await _late; Call("in-next"); }

            // A loop with no flow of the test's reads what is open when it handles a message
            public async Task ConsumerLoop() { await Publishes(); await Waits(); }
            [Step] async Task Publishes() { await Task.Yield(); Call("published"); }
            [Step] async Task Waits() { await Elsewhere("consumed"); Call("in-waits"); }

            // A service's thread with two top-level steps at once and nothing waiting
            public async Task ServiceThreadBetweenTwo()
            {
                var a = Beside(); var b = BesideToo();
                await _both.Task;
                await Elsewhere("from-the-service");
                _go.SetResult();
                await Task.WhenAll(a, b);
            }
            [Step] async Task Beside() { await _go.Task; Call("in-beside"); }
            [Step] async Task BesideToo() { _both.SetResult(); await _go.Task; Call("in-beside-too"); }

            // A trace a whole test shares: the same party calling again is no part of the call before
            private void Respond(string where, string caller, string service, Guid trace, Guid call) => Log(where, caller, service, RequestResponseType.Response, trace, call);

            public async Task SharedTraceAcrossSteps() { var trace = Guid.NewGuid(); var call = Guid.NewGuid(); await OpensIt(trace, call); await NextOnTheTrace(trace, call); }
            [Step] async Task OpensIt(Guid trace, Guid call)
            {
                await Task.Yield();
                Log("browser-first", "Browser", "Api", RequestResponseType.Request, trace, call);
                Log("api-to-slow", "Api", "Slow", RequestResponseType.Request, trace, _slow);
            }
            private readonly Guid _slow = Guid.NewGuid();
            [Step] async Task NextOnTheTrace(Guid trace, Guid call)
            {
                await Task.Yield();
                await OnAThreadOfItsOwn(() => Log("browser-second", "Browser", "Api", RequestResponseType.Request, trace, Guid.NewGuid()));
                Respond("api-to-slow", "Api", "Slow", trace, _slow);
                Respond("browser-first", "Browser", "Api", trace, call);
            }
            private Task OnAThreadOfItsOwn(Action act)
            {
                using (ExecutionContext.SuppressFlow())
                    return Task.Run(act);
            }

            // A trace a whole test shares, two steps at once: the call a service's work ran inside is the one made to that service
            private readonly Guid _sharedTrace = Guid.NewGuid();
            public async Task SharedTraceTwoParties() { var a = CallsSvc(); var b = CallsOther(); await Task.WhenAll(a, b); }
            [Step] async Task CallsSvc()
            {
                await Task.Yield();
                var call = Guid.NewGuid();
                Log("to-svc", "Tests", _calledAs, RequestResponseType.Request, _sharedTrace, call);
                _firstInFlight.SetResult();
                await _secondInFlight.Task;
                await Onward("svc-onward", "Svc", _sharedTrace);
                Respond("to-svc", "Tests", _calledAs, _sharedTrace, call);
                _go.SetResult();
            }
            [Step] async Task CallsOther()
            {
                await _firstInFlight.Task;
                var call = Guid.NewGuid();
                Log("to-other", "Tests", "Other", RequestResponseType.Request, _sharedTrace, call);
                _secondInFlight.SetResult();
                await _go.Task;
                Respond("to-other", "Tests", "Other", _sharedTrace, call);
            }

            // The same, with the service called something else in another case
            public Task SharedTraceOtherCase() { _calledAs = "SVC"; return SharedTraceTwoParties(); }
            private string _calledAs = "Svc";

            // Names that do not line up: a client that never named the service reads it from the URL
            public async Task UnalignedNames() { var a = UnalignedFirst(); var b = UnalignedSecond(); await Task.WhenAll(a, b); }
            [Step] async Task UnalignedFirst() { await Task.Yield(); await ThroughUnaligned("unaligned-first", _firstInFlight, _secondInFlight); }
            [Step] async Task UnalignedSecond() { await Task.Yield(); await ThroughUnaligned("unaligned-second", _secondInFlight, _firstInFlight); }
            private async Task ThroughUnaligned(string name, TaskCompletionSource mine, TaskCompletionSource other)
            {
                var trace = Guid.NewGuid();
                var call = Guid.NewGuid();
                Log(name, "Tests", "svc.local", RequestResponseType.Request, trace, call);
                mine.SetResult();
                await other.Task;
                await Onward(name + "-onward", "Svc", trace);
                Respond(name, "Tests", "svc.local", trace, call);
            }

            // A step on the far side of a call, with another step started since: it stands under the step that called
            public async Task AServicesStep() { var a = CallsTheService(); var b = StartedSince(); await Task.WhenAll(a, b); }
            [Step] async Task CallsTheService()
            {
                await Task.Yield();
                var trace = Guid.NewGuid();
                var call = Guid.NewGuid();
                Log("to-the-service", "Tests", "Svc", RequestResponseType.Request, trace, call);
                _firstInFlight.SetResult();
                await _secondInFlight.Task;
                await ServiceHandles(trace);
                Respond("to-the-service", "Tests", "Svc", trace, call);
                _go.SetResult();
            }
            [Step] async Task StartedSince() { await _firstInFlight.Task; _secondInFlight.SetResult(); await _go.Task; }
            private Task ServiceHandles(Guid trace)
            {
                using (ExecutionContext.SuppressFlow())
                    return Task.Run(async () =>
                    {
                        using var _ = BeginServiceScope(trace);
                        await TheHandler(trace);
                    });
            }
            [Step] async Task TheHandler(Guid trace) { await Task.Yield(); Log("from-the-handler", "Svc", "Svc2", RequestResponseType.Request, trace, Guid.NewGuid()); }

            // The same, the call answered already: that call is over
            public async Task AServicesStepAfterTheAnswer() { var a = CallsAndIsAnswered(); var b = StartedSinceTheAnswer(); await Task.WhenAll(a, b); }
            [Step] async Task CallsAndIsAnswered()
            {
                await Task.Yield();
                var trace = Guid.NewGuid();
                var call = Guid.NewGuid();
                Log("answered-call", "Tests", "Svc", RequestResponseType.Request, trace, call);
                Respond("answered-call", "Tests", "Svc", trace, call);
                _firstInFlight.SetResult();
                await _secondInFlight.Task;
                await ServiceHandles(trace);
                _go.SetResult();
            }
            [Step] async Task StartedSinceTheAnswer() { await _firstInFlight.Task; _secondInFlight.SetResult(); await _go.Task; }

            // A long-open call: what its service does once its step has ended is the step open then
            public async Task WorkAfterALongOpenCallsStep() { var trace = Guid.NewGuid(); var call = Guid.NewGuid(); await OpensAStream(trace, call); await Later2(trace); Log("stream", "Browser", "Api", RequestResponseType.Response, trace, call); }
            [Step] async Task OpensAStream(Guid trace, Guid call) { await Task.Yield(); Log("stream", "Browser", "Api", RequestResponseType.Request, trace, call); }
            [Step] async Task Later2(Guid trace) { await Task.Yield(); await Onward("api-to-db", "Api", trace); }

            // A request never answered: what its service did inside it is still inside it
            public async Task UnansweredParent() { var a = UnansweredFirst(); var b = UnansweredSecond(); await Task.WhenAll(a, b); }
            [Step] async Task UnansweredFirst()
            {
                await Task.Yield();
                var trace = Guid.NewGuid();
                Log("timed-out", "Tests", "Svc", RequestResponseType.Request, trace, Guid.NewGuid());
                _firstInFlight.SetResult();
                await _secondInFlight.Task;
                await Onward("timed-out-onward", "Svc", trace);
                _go.SetResult();
            }
            [Step] async Task UnansweredSecond() { await Task.Yield(); _secondInFlight.SetResult(); await _go.Task; }

            // A step whose child has ended is a leaf again
            private readonly TaskCompletionSource _childEnded = new();
            public async Task ServiceThreadAfterAChildEnded()
            {
                var a = WithAChild(); var b = BesideIt();
                await _both.Task;
                await Elsewhere("from-the-service");
                _go.SetResult();
                await Task.WhenAll(a, b);
            }
            [Step] async Task WithAChild() { await AChild(); _childEnded.SetResult(); await _go.Task; }
            [Step] async Task AChild() => await Task.Yield();
            [Step] async Task BesideIt() { await _childEnded.Task; _both.SetResult(); await _go.Task; }

            // A call made in a sub-step reads it
            public async Task InSubSteps() { await Outer(); await Later(); }
            [Step] async Task Outer() { Call("outer-before"); await Inner(); Call("outer-after"); }
            [Step] async Task Inner() { await Task.Yield(); Call("in-inner"); await Deepest(); }
            [Step] async Task Deepest() { await Task.Yield(); Call("in-deepest"); }
            [Step] async Task Later() { await Task.Yield(); Call("in-later"); }

            // A service's thread: the deepest of one chain, the nearest shared of two
            public async Task ServiceThreadOneChain() => await ChainOuter();
            [Step] async Task ChainOuter() => await ChainInner();
            [Step] async Task ChainInner() { await Task.Yield(); await Elsewhere("from-the-service"); }

            public async Task ServiceThreadTwoBranches() => await Shared();
            [Step] async Task Shared()
            {
                var a = BranchA(); var b = BranchB();
                await _both.Task;
                await Elsewhere("from-the-service");
                _go.SetResult();
                await Task.WhenAll(a, b);
            }
            [Step] async Task BranchA() { await _go.Task; }
            [Step] async Task BranchB() { _both.SetResult(); await _go.Task; }

            // The trace join: a service's onward call reads the step of the call it ran inside
            private void Log(string where, string caller, string service, RequestResponseType type, Guid trace, Guid call) =>
                RequestResponseLogger.Log(new RequestResponseLog(Id, Id, HttpMethod.Get, null, new Uri("http://svc.local/" + where), [], service, caller, type, trace, call, false));

            private Task Onward(string where, string caller, Guid trace)
            {
                var call = Guid.NewGuid();
                using (ExecutionContext.SuppressFlow())
                    return Task.Run(() =>
                    {
                        Log(where, caller, "Svc2", RequestResponseType.Request, trace, call);
                        Log(where, caller, "Svc2", RequestResponseType.Response, trace, call);
                    });
            }

            private readonly TaskCompletionSource _firstInFlight = new();
            private readonly TaskCompletionSource _secondInFlight = new();
            private async Task Through(string name, TaskCompletionSource mine, TaskCompletionSource other)
            {
                var trace = Guid.NewGuid();
                var call = Guid.NewGuid();
                Log(name, "Tests", "Svc", RequestResponseType.Request, trace, call);
                mine.SetResult();
                await other.Task;
                await Onward(name + "-onward", "Svc", trace);
                Log(name, "Tests", "Svc", RequestResponseType.Response, trace, call);
            }

            public async Task TraceJoin() { var a = CallsFirst(); var b = CallsSecond(); await Task.WhenAll(a, b); }
            [Step] async Task CallsFirst() { await Task.Yield(); await Through("first", _firstInFlight, _secondInFlight); }
            [Step] async Task CallsSecond() { await Task.Yield(); await Through("second", _secondInFlight, _firstInFlight); }

            // Below the top level
            public async Task TwoSubStepsThrough() => await BothThrough();
            [Step] async Task BothThrough() { var a = CallsFirst(); var b = CallsSecond(); await Task.WhenAll(a, b); }

            public async Task ParentsCallWhileAChildRuns() => await JoinParent();
            public Task ParentsCallUnalignedWhileAChildRuns() { _calledAs = "svc.local"; return ParentsCallWhileAChildRuns(); }
            [Step] async Task JoinParent()
            {
                var trace = Guid.NewGuid();
                var call = Guid.NewGuid();
                Log("parent-call", "Tests", _calledAs, RequestResponseType.Request, trace, call);
                var child = JoinChild();
                await _firstInFlight.Task;
                await Onward("parent-onward", "Svc", trace);
                _go.SetResult();
                await child;
                Log("parent-call", "Tests", _calledAs, RequestResponseType.Response, trace, call);
            }
            [Step] async Task JoinChild() { _firstInFlight.SetResult(); await _go.Task; }

            // Answered already: that call is over, and what ran on is no part of it
            public async Task AnsweredIsNotAParent() { var a = AnsweredFirst(); var b = AnsweredSecond(); await Task.WhenAll(a, b); }
            [Step] async Task AnsweredFirst()
            {
                await Task.Yield();
                var trace = Guid.NewGuid();
                var call = Guid.NewGuid();
                Log("answered", "Tests", "Svc", RequestResponseType.Request, trace, call);
                Log("answered", "Tests", "Svc", RequestResponseType.Response, trace, call);
                _firstInFlight.SetResult();
                await _secondInFlight.Task;
                await Onward("after-the-answer", "Svc", trace);
                _go.SetResult();
            }
            [Step] async Task AnsweredSecond() { await Task.Yield(); _secondInFlight.SetResult(); await _go.Task; }

            // The same party on the same trace is its own caller's sibling, not its child
            public async Task SameParty() { var a = SameFirst(); var b = SameSecond(); await Task.WhenAll(a, b); }
            [Step] async Task SameFirst()
            {
                await Task.Yield();
                var trace = Guid.NewGuid();
                var call = Guid.NewGuid();
                Log("same-party", "Svc", "Svc2", RequestResponseType.Request, trace, call);
                _firstInFlight.SetResult();
                await _secondInFlight.Task;
                await Onward("same-party-onward", "Svc", trace);
                Log("same-party", "Svc", "Svc2", RequestResponseType.Response, trace, call);
                _go.SetResult();
            }
            [Step] async Task SameSecond() { await Task.Yield(); _secondInFlight.SetResult(); await _go.Task; }
        }
        """;

    private static readonly Lazy<Type> Woven = new(() =>
    {
        var path = TestAssemblyBuilder.Build("StepFlowReading", Source);
        new StepWeaver().Weave(path, Path.ChangeExtension(path, ".pdb"));
        return Assembly.LoadFrom(path).GetType("Steps")!;
    });

    private static async Task<Dictionary<string, string?>> ReadCalls(string shape)
    {
        var id = $"reading-{shape}-{Guid.NewGuid():N}";
        using (TestIdentityScope.Begin(id, id))
        {
            var steps = Activator.CreateInstance(Woven.Value)!;
            Woven.Value.GetField("Id")!.SetValue(steps, id);
            Woven.Value.GetField("BeginServiceScope")!.SetValue(steps, (Func<Guid, IDisposable>)(trace => TestIdentityScope.Begin("service", id, trace)));
            await (Task)Woven.Value.GetMethod(shape)!.Invoke(steps, null)!;
        }

        return Read(id);
    }

    private static Dictionary<string, string?> Read(string id)
    {
        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(log => log.TestId == id).ToArray();
        Feature[] features = [new Feature { DisplayName = "F", Scenarios = [new Scenario { Id = id, DisplayName = "S", Steps = StepCollector.GetSteps(id) }] }];
        var paths = ReportGenerator.AttributeInteractionsToStepPaths(logs, features)[id];
        var calls = logs.Where(log => !log.IsDiagramMarker).ToArray();
        var read = new Dictionary<string, string?>();
        for (var i = 0; i < calls.Length; i++)
            read[calls[i].Uri.AbsolutePath.TrimStart('/')] = paths[i];
        return read;
    }


    [Fact]
    public void A_retried_scenarios_steps_and_calls_read_under_each_attempts_own_id()
    {
        var first = $"reading-attempt-{Guid.NewGuid():N}";
        var second = $"reading-attempt-{Guid.NewGuid():N}";

        // One flow, as a runner's retry loop has it, with the first attempt's step still open in that flow.
        using (TestIdentityScope.Begin(first, first))
        {
            StepCollector.StartStep(null, "the first attempt's step", null, null);
            LogCall(first, "in-the-first-attempt");
        }

        using (TestIdentityScope.Begin(second, second))
        {
            StepCollector.StartStep(null, "the second attempt's step", null, null);
            LogCall(second, "in-the-second-attempt");
            StepCollector.CompleteStep(true, null);
            LogCall(second, "after-the-second-attempts-step");
        }

        Assert.Equal("0", Read(first)["in-the-first-attempt"]);
        Assert.Equal("0", Read(second)["in-the-second-attempt"]);
        Assert.Equal("0", Read(second)["after-the-second-attempts-step"]);
        Assert.Equal(["the second attempt's step"], StepCollector.GetSteps(second).Select(s => s.Text));
    }

    private static void LogCall(string id, string where) =>
        RequestResponseLogger.Log(new RequestResponseLog(id, id, HttpMethod.Get, null, new Uri("http://svc.local/" + where), [], "Svc", "Tests", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false));

    [Fact]
    public async Task Two_top_level_steps_at_once_each_call_reads_its_own_step()
    {
        var read = await ReadCalls("TopLevelAtOnce");

        Assert.Equal("0", read["in-first"]);
        Assert.Equal("1", read["in-second"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_call_between_two_steps_stays_with_the_bar_above_it_bars_on_or_off(bool shown)
    {
        var options = StepCollector.Options;
        StepCollector.Options = new StepTrackingOptions { ShowStepDelimiters = shown };
        try
        {
            var read = await ReadCalls("BetweenSteps");

            Assert.Equal("0", read["in-one"]);
            Assert.Equal("0", read["between"]);
            Assert.Equal("1", read["in-two"]);
            Assert.Equal("1", read["after-two"]);
        }
        finally
        {
            StepCollector.Options = options;
        }
    }

    [Fact]
    public async Task A_late_call_reads_the_step_open_at_the_time()
    {
        var read = await ReadCalls("LateCall");

        Assert.Equal("0", read["in-starter"]);
        Assert.Equal("1", read["detached"]);
        Assert.Equal("1", read["in-next"]);
    }

    [Fact]
    public async Task A_hosts_consumer_loop_reads_the_step_open_when_the_message_is_handled()
    {
        var read = await ReadCalls("ConsumerLoop");

        Assert.Equal("0", read["published"]);
        Assert.Equal("1", read["consumed"]);
    }

    [Fact]
    public async Task A_record_from_a_services_thread_with_two_top_level_steps_at_once_and_nothing_waiting_reads_no_step()
    {
        var read = await ReadCalls("ServiceThreadBetweenTwo");

        Assert.Null(read["from-the-service"]);
        Assert.Equal("0", read["in-beside"]);
        Assert.Equal("1", read["in-beside-too"]);
    }

    [Fact]
    public async Task A_trace_a_whole_test_shares_does_not_move_a_call_to_an_earlier_step()
    {
        var read = await ReadCalls("SharedTraceAcrossSteps");

        Assert.Equal("0", read["browser-first"]);
        Assert.Equal("1", read["browser-second"]);
    }

    [Fact]
    public async Task On_a_trace_a_whole_test_shares_a_services_onward_call_reads_the_call_made_to_that_service()
    {
        var read = await ReadCalls("SharedTraceTwoParties");

        Assert.Equal("0", read["to-svc"]);
        Assert.Equal("1", read["to-other"]);
        Assert.Equal("0", read["svc-onward"]);
    }

    [Fact]
    public async Task The_party_called_is_told_whatever_the_case_of_its_name()
    {
        var read = await ReadCalls("SharedTraceOtherCase");

        Assert.Equal("0", read["svc-onward"]);
    }

    [Fact]
    public async Task A_services_onward_call_reads_the_call_it_ran_inside_when_the_names_do_not_line_up()
    {
        var read = await ReadCalls("UnalignedNames");

        Assert.Equal("0", read["unaligned-first-onward"]);
        Assert.Equal("1", read["unaligned-second-onward"]);
    }

    [Fact]
    public async Task A_step_on_the_far_side_of_a_call_stands_under_the_step_that_called()
    {
        var read = await ReadCalls("AServicesStep");

        Assert.Equal("0", read["to-the-service"]);
        Assert.Equal("0.0", read["from-the-handler"]);
    }

    [Fact]
    public async Task A_step_on_the_far_side_of_a_call_already_answered_goes_under_what_is_running()
    {
        var read = await ReadCalls("AServicesStepAfterTheAnswer");

        Assert.Equal("0", read["answered-call"]);
        Assert.Equal("1.0", read["from-the-handler"]);
    }

    [Fact]
    public async Task What_a_service_does_after_a_long_open_calls_step_has_ended_reads_the_step_open_then()
    {
        var read = await ReadCalls("WorkAfterALongOpenCallsStep");

        Assert.Equal("0", read["stream"]);
        Assert.Equal("1", read["api-to-db"]);
    }

    [Fact]
    public async Task An_onward_call_under_a_request_never_answered_reads_that_requests_step()
    {
        Assert.Equal("0", (await ReadCalls("UnansweredParent"))["timed-out-onward"]);
    }

    [Fact]
    public async Task A_step_whose_child_has_ended_still_counts_as_one_of_the_steps_running()
    {
        var read = await ReadCalls("ServiceThreadAfterAChildEnded");

        Assert.Null(read["from-the-service"]);
    }

    [Fact]
    public async Task A_call_reads_the_deepest_step_it_was_made_in()
    {
        var read = await ReadCalls("InSubSteps");

        Assert.Equal("0", read["outer-before"]);
        Assert.Equal("0.0", read["in-inner"]);
        Assert.Equal("0.0.0", read["in-deepest"]);
        Assert.Equal("0", read["outer-after"]);
        Assert.Equal("1", read["in-later"]);
    }

    [Fact]
    public async Task A_services_thread_with_one_chain_open_reads_the_deepest_step()
    {
        Assert.Equal("0.0", (await ReadCalls("ServiceThreadOneChain"))["from-the-service"]);
    }

    [Fact]
    public async Task A_services_thread_with_two_branches_open_reads_the_step_they_share()
    {
        Assert.Equal("0", (await ReadCalls("ServiceThreadTwoBranches"))["from-the-service"]);
    }

    [Fact]
    public async Task Two_sub_steps_at_once_each_services_onward_call_reads_the_sub_step_it_ran_inside()
    {
        var read = await ReadCalls("TwoSubStepsThrough");

        Assert.Equal("0.0", read["first-onward"]);
        Assert.Equal("0.1", read["second-onward"]);
    }

    [Fact]
    public async Task A_parents_own_call_keeps_what_the_service_did_for_it_while_a_child_runs_when_the_names_do_not_line_up()
    {
        var read = await ReadCalls("ParentsCallUnalignedWhileAChildRuns");

        Assert.Equal("0", read["parent-call"]);
        Assert.Equal("0", read["parent-onward"]);
    }

    [Fact]
    public async Task A_parents_own_call_keeps_what_the_service_did_for_it_while_a_child_runs()
    {
        var read = await ReadCalls("ParentsCallWhileAChildRuns");

        Assert.Equal("0", read["parent-call"]);
        Assert.Equal("0", read["parent-onward"]);
    }

    [Fact]
    public async Task Two_top_level_steps_at_once_each_services_onward_call_reads_the_step_of_the_call_it_ran_inside()
    {
        var read = await ReadCalls("TraceJoin");

        Assert.Equal("0", read["first"]);
        Assert.Equal("0", read["first-onward"]);
        Assert.Equal("1", read["second"]);
        Assert.Equal("1", read["second-onward"]);
    }

    [Fact]
    public async Task A_call_already_answered_is_not_the_parent_of_what_ran_on()
    {
        var read = await ReadCalls("AnsweredIsNotAParent");

        Assert.Equal("0", read["answered"]);
        Assert.Null(read["after-the-answer"]);
    }

    [Fact]
    public async Task A_trace_shared_with_the_same_party_is_not_a_parent()
    {
        var read = await ReadCalls("SameParty");

        Assert.Equal("0", read["same-party"]);
        Assert.Null(read["same-party-onward"]);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public class StepOptionsCollection
{
    public const string Name = "Step options";
}
