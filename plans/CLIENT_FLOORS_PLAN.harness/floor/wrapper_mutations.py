# The wrapper guards' mutation check (plans/CLIENT_FLOORS_PLAN.md R2b): each mutation removes one override a tracking
# client has, and the guard fact that holds the wrapper to the members known to come back must then fail, naming that
# member. The file is restored after each run.
#   python -I wrapper_mutations.py <worktree> > wrapper-mutations.txt
import pathlib, re, subprocess, sys

worktree = pathlib.Path(sys.argv[1])
MUTATIONS = [
    ("PubSub", "src/Kronikol.Extensions.PubSub/TrackingSubscriberClient.cs",
     r"\n    public override Task StartAsync\(SubscriptionHandler handler\)[^\n]*\n",
     "The_subscriber_leaves_to_its_base_only_overloads_shown_to_come_back", "StartAsync(SubscriptionHandler)"),
    ("ServiceBus", "src/Kronikol.Extensions.ServiceBus/TrackingServiceBusClient.cs",
     r"\n    public override ServiceBusRuleManager CreateRuleManager\(string topicName, string subscriptionName\)\n[^\n]*\n",
     "The_client_leaves_to_its_base_only_the_members_this_class_names", "CreateRuleManager(String, String)"),
    ("ServiceBus", "src/Kronikol.Extensions.ServiceBus/TrackingServiceBusSender.cs",
     r"\n    public override ValueTask DisposeAsync\(\)[^\n]*\n",
     "The_sender_leaves_nothing_to_its_base", "DisposeAsync()"),
    ("EventHubs", "src/Kronikol.Extensions.EventHubs/TrackingEventHubConsumerClient.cs",
     r"\n    public override IAsyncEnumerable<PartitionEvent> ReadEventsAsync\(\n        bool startReadingAtEarliestEvent,[^;]*;\n",
     "The_consumer_leaves_to_its_base_only_what_comes_back_to_an_override", "ReadEventsAsync(Boolean, ReadEventOptions, CancellationToken)"),
    ("EventHubs", "src/Kronikol.Extensions.EventHubs/TrackingEventHubProducerClient.cs",
     r"\n    public override async Task<PartitionProperties> GetPartitionPropertiesAsync\(\n[^;]*;\n",
     "The_producer_leaves_to_its_base_only_what_comes_back_to_an_override", "GetPartitionPropertiesAsync(String, CancellationToken)"),
]

for package, source, pattern, fact, member in MUTATIONS:
    path = worktree / source
    original = path.read_bytes()
    text = original.decode("utf-8")
    mutated, count = re.subn(pattern, "\n", text, count=1)
    if count != 1:
        print(f"{package} | {member} | MUTATION NOT APPLIED (pattern did not match)", flush=True)
        continue
    try:
        path.write_bytes(mutated.encode("utf-8"))
        run = subprocess.run(
            ["dotnet", "test", str(worktree / f"tests/Kronikol.Tests.{package}"), "-nologo", "-nodeReuse:false",
             "--filter", f"FullyQualifiedName~{fact}"],
            capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=1200)
        out = run.stdout + run.stderr
        summary = next((l.strip() for l in out.splitlines() if "Failed!" in l or "Passed!" in l), "no summary: " + out[-300:])
        named = member in out
        verdict = "caught" if "Failed!" in summary and named else "NOT CAUGHT"
        print(f"{package} | remove {member} | {fact}: {verdict} | named: {named} | {summary.split(' - ')[0]}", flush=True)
    finally:
        path.write_bytes(original)
