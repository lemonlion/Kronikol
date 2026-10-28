using System.Security.Cryptography;
using System.Text;
using Kronikol.ComponentDiagram;
using Kronikol.Constants;
using Kronikol.History;
using Kronikol.Parties;

namespace Kronikol.CallGraph;

/// <summary>
/// The parties a graph reads: the report's block as written, or none, in which case they are inferred from the calls
/// (plans/SERVICE_CARD_PLAN.md 4.2, Q15). What a run observed and did not write is never an input.
/// </summary>
internal sealed record PartiesInput(IReadOnlyList<Party>? Block, string? ServiceId)
{
    /// <summary>No block: every row is inferred from the calls.</summary>
    internal static PartiesInput Inferred { get; } = new(null, null);
}

/// <summary>
/// Builds a <see cref="Graph"/> from a run's records (plans/SERVICE_CARD_PLAN.md 4.2): a pure function of them, its parties
/// and the consumer's templating rules, so a report gives one graph however often it is read.
/// </summary>
internal static class CallGraphBuilder
{
    /// <param name="scenarios">Every scenario of the run, with calls or without.</param>
    /// <param name="records">Every record, each scenario's in its list's order and the root background block's in its own.</param>
    /// <param name="parties">The report's block, or none.</param>
    /// <param name="rules">The consumer's templating rules the report recorded.</param>
    /// <param name="templateRules">What a card says of those rules: their hash, <c>"none"</c>, or null for a report written before it recorded them.</param>
    /// <param name="callsRecorded">False for a report that cannot carry calls, a mergeable file written before 3.1.0.</param>
    internal static Graph Build(IReadOnlyList<CallScenario> scenarios, IReadOnlyList<CallRecord> records, PartiesInput parties,
        HistoryShapeRules? rules = null, string? templateRules = null, bool callsRecorded = true)
    {
        var calls = Calls(records);
        var (table, rows, subjectRow, inference) = GraphParties.Resolve(parties,
            [.. calls.Select(c => new PartyCall(c.Request.Caller, c.Request.Service, c.Request.Category, c.Request.CallerCategory, c.Request.AttributionSource, c.Request.TestDouble))]);
        var kinds = rows.ToDictionary(r => r.Name, r => r.Kind, StringComparer.Ordinal);
        var subject = subjectRow?.Name;
        var hasTestParty = table.Parties.Any(r => r.Kind == PartyKind.Test);
        PartyKind KindOf(string name) => kinds.GetValueOrDefault(name, PartyKind.Unknown);

        // Origin, cause and the effective phase. A parent is always an earlier request of the same list, so it is read first.
        foreach (var call in calls)
        {
            var request = call.Request;
            // Who and why are the party table's (#192 R3), the rules the query engine's origin and cause read.
            call.Origin = table.OriginOf(request.Caller);
            call.Cause = table.CauseOf(request.Caller, request.AttributionSource, inBackgroundBlock: request.Scenario is null,
                insideReceivedCall: call.Parent is not null);
            (call.EffectivePhase, call.PhaseDerived) = Phase(request.Phase) is { } recorded ? (recorded, false)
                : call.Cause != "background" && call.Parent is { } parent && calls[parent].EffectivePhase != "unknown" ? (calls[parent].EffectivePhase, true)
                : ("unknown", false);
            call.Status = call.Response?.Status ?? "none";
            call.IsError = call.Response?.IsError ?? false;
        }

        // Operations, keyed by what they are, so a new one moves no other reference.
        var operations = new Dictionary<string, GraphOperation>(StringComparer.Ordinal);
        foreach (var call in calls)
        {
            var request = call.Request;
            var method = request.Method ?? "";
            var protocol = ComponentDiagramGenerator.Protocol(request.Category, request.IsEvent, request.Method);
            var (target, graphql) = OperationKey.Parts(request.Method, request.Uri, request.Category, request.Content, rules, queryKeys: false);
            var op = OperationId(request.Caller, request.Service, protocol, method, target, graphql);
            call.Op = op;
            if (!operations.ContainsKey(op))
                operations[op] = new GraphOperation { Op = op, From = request.Caller, To = request.Service, Protocol = protocol, Method = method, Target = target, GraphqlOperation = graphql };
        }

        foreach (var group in calls.GroupBy(c => c.Op, StringComparer.Ordinal))
        {
            var operation = operations[group.Key];
            operation.Statuses = Sorted(group.Select(c => c.Status));
            operation.Origins = Sorted(group.Select(c => c.Origin));
            operation.Causes = Sorted(group.Select(c => c.Cause));
            operation.EffectivePhases = Sorted(group.Select(c => c.EffectivePhase));
            operation.Calls = group.Count();
            operation.Errors = group.Count(c => c.IsError);
            operation.StatusCounts = [.. group.GroupBy(c => c.Status, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => KeyValuePair.Create(g.Key, g.Count()))];
            operation.Setup = group.Count(c => c.EffectivePhase == "setup");
            operation.Action = group.Count(c => c.EffectivePhase == "action");
            operation.Unknown = group.Count(c => c.EffectivePhase == "unknown");
            operation.DerivedPhases = group.Count(c => c.PhaseDerived);
            operation.Example = group.Where(c => c.Request.Scenario is not null)
                .OrderBy(c => c.Request.Scenario!.Ordinal).ThenBy(c => c.Request.Position)
                .Select(c => c.Request.Address).FirstOrDefault();
        }

        // Messages: the subject's calls to a broker and a broker's deliveries into it, read per call.
        var messages = new HashSet<GraphMessage>();
        var emptyTargets = new SortedSet<string>(StringComparer.Ordinal);
        var deliveries = new HashSet<GraphCall>();
        foreach (var call in calls)
        {
            if (subject is null || !IsMessageCall(call.Request, subject))
                continue;

            var request = call.Request;
            var scheme = MessageTable.Scheme(request.Uri);
            string? direction;
            string broker;
            if (request.Service == subject)
            {
                deliveries.Add(call);
                // MassTransit logs a consume's fault into the consumer too: a delivery still, and no message.
                if (MessageTable.Read(scheme, request.Method) == MessageLabelKind.Neither)
                    continue;
                (direction, broker) = ("consumes", request.Caller);
            }
            else
            {
                broker = request.Service;
                var kind = MessageTable.HasScheme(scheme) ? MessageTable.Read(scheme, request.Method)
                    // SQS and SNS at Raw record the HTTP request itself, whose verb says nothing of the direction.
                    : scheme is "http" or "https" ? MessageLabelKind.Unknown
                    : MessageLabelKind.Publishes;
                if (kind == MessageLabelKind.Neither)
                    continue;
                direction = kind switch
                {
                    MessageLabelKind.Publishes => "publishes",
                    MessageLabelKind.Receives => "consumes",
                    _ => null
                };
            }

            if (MessageTable.IsEmptyDestination(request.Uri))
                emptyTargets.Add(call.Op);
            messages.Add(new GraphMessage(call.Op, direction, broker, MessageTable.Destination(request.Uri)));
        }

        var graphParties = PartiesOf(rows, calls, deliveries);
        var exposes = subject is null ? [] : calls.Where(c => c.Request.Service == subject).GroupBy(c => c.Op, StringComparer.Ordinal)
            .Where(g => !g.Any(c => deliveries.Contains(c) || c.Request.IsUserAction))
            .Select(g => g.Key).Order(StringComparer.Ordinal).ToList();

        return new Graph
        {
            Calls = calls,
            Parties = graphParties,
            Subject = subject is null ? null : graphParties.Single(p => p.Name == subject),
            Edges = operations.Values.GroupBy(o => (o.From, o.To, o.Protocol))
                .OrderBy(g => g.Key.From, StringComparer.Ordinal).ThenBy(g => g.Key.To, StringComparer.Ordinal).ThenBy(g => g.Key.Protocol, StringComparer.Ordinal)
                .Select(g => new GraphEdge(g.Key.From, g.Key.To, g.Key.Protocol) { Operations = OperationOrder(g) })
                .ToList(),
            Operations = operations.Values.OrderBy(o => o.Op, StringComparer.Ordinal).ToList(),
            Messages = messages.OrderBy(m => m.Party, StringComparer.Ordinal).ThenBy(m => m.Topic, StringComparer.Ordinal)
                .ThenBy(m => m.Direction, StringComparer.Ordinal).ThenBy(m => m.Op, StringComparer.Ordinal).ToList(),
            Exposes = exposes,
            Scenarios = ScenariosOf(scenarios, calls, hasTestParty, KindOf),
            CallsRecorded = callsRecorded,
            SubjectCallsRecorded = subject is not null && calls.Any(c => c.Request.Caller == subject),
            EmptyTargets = [.. emptyTargets],
            Rules = new GraphRules(OperationKey.Version, templateRules, inference)
        };
    }

    /// <summary>
    /// One call a request that names both its parties, in the records' order, each with the first response carrying its
    /// id in its own list and the call it ran inside, by <see cref="NestingRule"/> over that list.
    /// </summary>
    private static List<GraphCall> Calls(IReadOnlyList<CallRecord> records)
    {
        var calls = new List<GraphCall>();
        var lists = new List<List<CallRecord>>();
        var listOf = new Dictionary<int, List<CallRecord>>();
        foreach (var record in records)
        {
            var key = record.Scenario?.Ordinal ?? -1;
            if (!listOf.TryGetValue(key, out var list))
            {
                list = [];
                listOf[key] = list;
                lists.Add(list);
            }
            list.Add(record);
        }

        foreach (var list in lists)
        {
            var parents = NestingRule.Parents([.. list.Select(r => new NestingRecord(r.IsRequest, !r.IsRequest, r.RequestResponseId, r.Caller, r.Service, r.TraceId))]);
            var responses = new Dictionary<string, CallRecord>(StringComparer.Ordinal);
            foreach (var record in list)
                if (!record.IsRequest && record.RequestResponseId is { } id)
                    responses.TryAdd(id, record);

            var callAt = new Dictionary<int, int>();
            for (var i = 0; i < list.Count; i++)
            {
                var record = list[i];
                if (!record.IsRequest || record.Caller.Length == 0 || record.Service.Length == 0)
                    continue;
                callAt[i] = calls.Count;
                calls.Add(new GraphCall
                {
                    Request = record,
                    Response = record.RequestResponseId is { } id && responses.TryGetValue(id, out var response) ? response : null,
                    Parent = parents[i] is { } parent && callAt.TryGetValue(parent, out var at) ? at : null
                });
            }
        }

        return calls;
    }

    private static string? Phase(string? recorded) => recorded switch
    {
        "Setup" => "setup",
        "Action" => "action",
        _ => null
    };

    /// <summary>
    /// A call between the subject and another party that carries <c>MessageQueue</c> on either side, or whose scheme is a
    /// row of the message table (Service Bus writes a category of its own). The meta type is no sign either way: Spanner,
    /// BigQuery and Bigtable write Event on calls that are no messages, and the generic <c>MessageTracker</c> on its publishes.
    /// </summary>
    private static bool IsMessageCall(CallRecord request, string subject) =>
        (request.Caller == subject) != (request.Service == subject)
        && (string.Equals(request.Category, DependencyCategories.MessageQueue, StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.CallerCategory, DependencyCategories.MessageQueue, StringComparison.OrdinalIgnoreCase)
            || MessageTable.HasScheme(MessageTable.Scheme(request.Uri)));

    /// <summary>The first 16 hex of SHA-256 over the operation's parts, joined by U+001F.</summary>
    private static string OperationId(string from, string to, string protocol, string method, string target, string? graphql)
    {
        var parts = graphql is null ? new[] { from, to, protocol, method, target } : [from, to, protocol, method, target, graphql];
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001F', parts))))[..16];
    }

    private static List<GraphOperation> OperationOrder(IEnumerable<GraphOperation> operations) =>
        [.. operations.OrderBy(o => o.Method, StringComparer.Ordinal).ThenBy(o => o.Target, StringComparer.Ordinal).ThenBy(o => o.GraphqlOperation, StringComparer.Ordinal)];

    private static List<string> Sorted(IEnumerable<string> values) => [.. values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>
    /// Each party with the categories its calls carried, the hosts it was reached at and how many of its calls a test
    /// double answered. A delivery's category and host are its broker's: the record names the broker as its caller and the
    /// consumer as its service, and the address is the broker's.
    /// </summary>
    private static List<GraphParty> PartiesOf(IReadOnlyList<Party> rows, List<GraphCall> calls, HashSet<GraphCall> deliveries)
    {
        var categories = rows.ToDictionary(r => r.Name, _ => new SortedSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var hosts = rows.ToDictionary(r => r.Name, _ => new SortedSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var doubles = new Dictionary<string, int>(StringComparer.Ordinal);
        var received = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var call in calls)
        {
            var request = call.Request;
            var delivery = deliveries.Contains(call);
            var reached = delivery ? request.Caller : request.Service;
            if (!string.IsNullOrEmpty(request.Category))
                categories[reached].Add(request.Category);
            if (!string.IsNullOrEmpty(request.CallerCategory))
                categories[request.Caller].Add(request.CallerCategory);
            if (!MessageTable.AuthorityIsDestination(MessageTable.Scheme(request.Uri)) && MessageTable.Authority(request.Uri) is { } host)
                hosts[reached].Add(host);
            received[request.Service] = received.GetValueOrDefault(request.Service) + 1;
            if (request.TestDouble is not null)
                doubles[request.Service] = doubles.GetValueOrDefault(request.Service) + 1;
        }

        return [.. rows.Select(r => new GraphParty(r.Name, r.Id, r.Kind, r.Kind == PartyKind.Unknown ? null : r.KindSource, r.Note)
        {
            Categories = [.. categories[r.Name]],
            Hosts = [.. hosts[r.Name]],
            DoubleCalls = doubles.GetValueOrDefault(r.Name),
            Received = received.GetValueOrDefault(r.Name)
        })];
    }

    /// <summary>
    /// The scenarios that are tests, by stable id, each with the test's own operations (the test party's calls, or with no
    /// test party the top-level calls that are not background) and every operation its calls reached but background ones.
    /// Rows sharing a stable id are one, named by the row that sorts first by feature, then scenario.
    /// </summary>
    private static List<GraphScenario> ScenariosOf(IReadOnlyList<CallScenario> scenarios, List<GraphCall> calls, bool hasTestParty, Func<string, PartyKind> kindOf)
    {
        var byOrdinal = calls.Where(c => c.Request.Scenario is not null).ToLookup(c => c.Request.Scenario!.Ordinal);
        return scenarios.Where(s => s.IsTest)
            .GroupBy(s => s.StableId, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var named = g.OrderBy(s => s.Feature, StringComparer.Ordinal).ThenBy(s => s.Name, StringComparer.Ordinal).First();
                var own = g.SelectMany(s => byOrdinal[s.Ordinal]).ToList();
                var ops = own.Where(c => hasTestParty ? kindOf(c.Request.Caller) == PartyKind.Test : c.Parent is null && c.Cause != "background").Select(c => c.Op);
                var all = own.Where(c => c.Cause != "background").Select(c => c.Op);
                return new GraphScenario(named, Sorted(ops), Sorted(all));
            })
            .ToList();
    }
}
