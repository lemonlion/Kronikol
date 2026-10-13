"""Mutation check for the #136 audit (plans/MONGODB_ACCESSOR_OPTION_PLAN.md section 12).

Each mutation undoes one changed line. The facts named must fail and no others; the file is restored afterwards.
Run from the repository root: python plans/MONGODB_ACCESSOR_OPTION_PLAN.harness/audit/mutate.py
"""
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[3]

MUTATIONS = [
    ("A1", "src/Kronikol.Extensions.BigQuery/BigQueryTracker.cs",
     "_httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;", "_httpContextAccessor = httpContextAccessor;",
     "BigQuery", {"BigQueryTrackerTests.Tracker_reads_HttpContextAccessor_from_options_when_not_passed_directly"}),
    ("A2", "src/Kronikol.Extensions.PubSub/PubSubTracker.cs",
     "_httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;", "_httpContextAccessor = httpContextAccessor;",
     "PubSub", {"PubSubTrackerTests.Tracker_reads_HttpContextAccessor_from_options_when_not_passed_directly"}),
    ("A3", "src/Kronikol.Extensions.Dapper/TrackingDbCommand.cs",
     "_httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;", "_httpContextAccessor = httpContextAccessor;",
     "Dapper", {"TrackingDbCommandTests.A_command_built_directly_reads_HttpContextAccessor_from_options"}),
    ("B1", "src/Kronikol.Extensions.BigQuery/BigQueryServiceCollectionExtensions.cs",
     "options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>()", "sp.GetService<IHttpContextAccessor>() ?? options.HttpContextAccessor",
     "BigQuery", {"BigQueryServiceCollectionExtensionsTests.Registration_prefers_the_options_accessor_to_the_containers"}),
    ("B2", "src/Kronikol.Extensions.BigQuery/BigQueryServiceCollectionExtensions.cs",
     "options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>()", "options.HttpContextAccessor",
     "BigQuery", {"BigQueryServiceCollectionExtensionsTests.Registration_uses_the_containers_accessor_when_the_options_carry_none"}),
    ("B3", "src/Kronikol.Extensions.PubSub/PubSubServiceCollectionExtensions.cs",
     "new PubSubTracker(options, options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>())",
     "new PubSubTracker(options, sp.GetService<IHttpContextAccessor>() ?? options.HttpContextAccessor)",
     "PubSub", {"PubSubServiceCollectionExtensionsTests.Registration_prefers_the_options_accessor_to_the_containers"}),
    ("B4", "src/Kronikol.Extensions.PubSub/PubSubServiceCollectionExtensions.cs",
     "new PubSubTracker(options, options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>())",
     "new PubSubTracker(options, options.HttpContextAccessor)",
     "PubSub", {"PubSubServiceCollectionExtensionsTests.Registration_uses_the_containers_accessor_when_the_options_carry_none"}),
    ("B5", "src/Kronikol.Extensions.AtlasDataApi/AtlasDataApiServiceCollectionExtensions.cs",
     "httpContextAccessor: options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>()",
     "httpContextAccessor: sp.GetService<IHttpContextAccessor>() ?? options.HttpContextAccessor",
     "AtlasDataApi", {"AtlasDataApiServiceCollectionExtensionsTests.Registration_prefers_the_options_accessor_to_the_containers"}),
    ("B6", "src/Kronikol.Extensions.AtlasDataApi/AtlasDataApiServiceCollectionExtensions.cs",
     "httpContextAccessor: options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>()",
     "httpContextAccessor: options.HttpContextAccessor",
     "AtlasDataApi", {"AtlasDataApiServiceCollectionExtensionsTests.Registration_uses_the_containers_accessor_when_the_options_carry_none"}),
    ("C1", "src/Kronikol.Extensions.PubSub/TrackingPublisherClient.cs",
     "ResolveWithSource(_tracker.HttpContextAccessor,", "ResolveWithSource(_options.HttpContextAccessor,",
     "PubSub", {"TrackingPublisherClientTests.PublishAsync_stamps_the_scenario_the_publish_is_logged_under_when_given_an_accessor"}),
    ("C2", "src/Kronikol.Extensions.ServiceBus/TrackingServiceBusSender.cs",
     "ResolveWithSource(_tracker.HttpContextAccessor,", "ResolveWithSource(_options.HttpContextAccessor,",
     "ServiceBus", {"TrackingServiceBusSenderIdentityTests.SendMessageAsync_stamps_the_scenario_the_send_is_logged_under_when_the_tracker_was_given_an_accessor"}),
    ("C3", "src/Kronikol.Extensions.MassTransit/TrackingSendObserver.cs",
     "ResolveWithSource(tracker.HttpContextAccessor,", "ResolveWithSource(options.HttpContextAccessor,",
     "MassTransit", {"TrackingObserverIdentityTests.PreSend_stamps_the_trackers_scenario_when_the_tracker_was_given_an_accessor"}),
    ("C4", "src/Kronikol.Extensions.MassTransit/TrackingPublishObserver.cs",
     "ResolveWithSource(tracker.HttpContextAccessor,", "ResolveWithSource(options.HttpContextAccessor,",
     "MassTransit", {"TrackingObserverIdentityTests.PrePublish_stamps_the_trackers_scenario_when_the_tracker_was_given_an_accessor"}),
    # Equivalent: the Event Hubs producer builds its tracker from the options' accessor, so both read the same one.
    # Only the source guard (No_extension_stamps_a_message_from_the_options_accessor_alone) holds this line.
    ("C5", "src/Kronikol.Extensions.EventHubs/TrackingEventHubProducerClient.cs",
     "ResolveWithSource(_tracker.HttpContextAccessor,", "ResolveWithSource(_options.HttpContextAccessor,",
     "EventHubs", set()),
    # The precedence facts the audit strengthened: they asserted only HasHttpContextAccessor, which held under these.
    ("D1", "src/Kronikol.Extensions.AtlasDataApi/AtlasDataApiTrackingMessageHandler.cs",
     "_httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;",
     "_httpContextAccessor = options.HttpContextAccessor ?? httpContextAccessor;",
     "core:HttpContextAccessorOptionsTests", {"HttpContextAccessorOptionsTests.AtlasDataApi_handler_explicit_accessor_takes_precedence_over_options"}),
    ("D2", "src/Kronikol.Extensions.BigQuery/BigQueryTrackingMessageHandler.cs",
     "_httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;",
     "_httpContextAccessor = options.HttpContextAccessor ?? httpContextAccessor;",
     "core:HttpContextAccessorOptionsTests", {"HttpContextAccessorOptionsTests.BigQuery_handler_explicit_accessor_takes_precedence_over_options"}),
]


def run(project):
    if project.startswith("core:"):
        command = ["dotnet", "test", "tests/Kronikol.Tests/Kronikol.Tests.csproj", "--filter",
                   "FullyQualifiedName~" + project.split(":", 1)[1]]
    else:
        command = ["dotnet", "test", f"tests/Kronikol.Tests.{project}/Kronikol.Tests.{project}.csproj"]
    proc = subprocess.run(
        command,
        cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace")
    failed = set()
    for line in proc.stdout.splitlines():
        m = re.search(r"\[FAIL\]\s*$", line)
        if m:
            name = line.split("]", 1)[1].replace("[FAIL]", "").strip()
            failed.add(".".join(name.split(".")[-2:]))
    if proc.returncode != 0 and not failed:
        raise RuntimeError(f"{project} failed without a failing fact:\n{proc.stdout[-3000:]}")
    return failed


def main():
    only = set(sys.argv[1:])
    ok = True
    for mid, rel, old, new, project, expected in MUTATIONS:
        if only and mid not in only:
            continue
        path = ROOT / rel
        original = path.read_bytes()
        text = original.decode("utf-8")
        assert text.count(old) == 1, (mid, text.count(old))
        try:
            path.write_bytes(text.replace(old, new).encode("utf-8"))
            failed = run(project)
        finally:
            path.write_bytes(original)
        verdict = "caught exactly" if failed == expected else "MISMATCH"
        ok &= failed == expected
        print(f"{mid} {rel}: {verdict}; failed {sorted(failed) or 'nothing'}", flush=True)
    print("ALL AS EXPECTED" if ok else "SOME MUTATIONS DID NOT MATCH")


if __name__ == "__main__":
    main()
