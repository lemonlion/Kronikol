# R2's mutations: each breaks one line of the fix, runs the test project that covers it, and records which facts fail.
# Run from the repository root:  PYTHONUTF8=1 python plans/MONGODB_ACCESSOR_OPTION_PLAN.harness/r2/mutate.py
import pathlib, re, subprocess, sys

S = pathlib.Path("src")
KT, KDI, KP = S / "Kronikol.Extensions.Kafka/KafkaTracker.cs", S / "Kronikol.Extensions.Kafka/KafkaServiceCollectionExtensions.cs", S / "Kronikol.Extensions.Kafka/TrackingKafkaProducer.cs"
BT, BDI = S / "Kronikol.Extensions.Bigtable/BigtableTracker.cs", S / "Kronikol.Extensions.Bigtable/BigtableServiceCollectionExtensions.cs"
ST, SDI = S / "Kronikol.Extensions.Spanner/SpannerTracker.cs", S / "Kronikol.Extensions.Spanner/SpannerServiceCollectionExtensions.cs"
MX = S / "Kronikol.Extensions.MediatR/MediatorTrackingExtensions.cs"
EI, EDI, EX = (S / "Kronikol.Extensions.EfCore.Relational/SqlTrackingInterceptor.cs",
               S / "Kronikol.Extensions.EfCore.Relational/ServiceCollectionExtensions.cs",
               S / "Kronikol.Extensions.EfCore.Relational/SqlTrackingInterceptorOptionsExtensions.cs")
BO = S / "Kronikol.Extensions.Bigtable/BigtableTrackingOptions.cs"

FALLBACK = "_httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;"
NO_FALLBACK = "_httpContextAccessor = httpContextAccessor;"
SWAPPED = "_httpContextAccessor = options.HttpContextAccessor ?? httpContextAccessor;"
KDI_LINE = "new KafkaTracker(options, options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>())"
KDI_SWAP = "new KafkaTracker(options, sp.GetService<IHttpContextAccessor>() ?? options.HttpContextAccessor)"
KDI_OPTS = "new KafkaTracker(options, options.HttpContextAccessor)"
PREFERS = "Registration_prefers_the_options_accessor_to_the_containers"
CONTAINER = "Registration_uses_the_containers_accessor_when_the_options_carry_none"
FALLBACK_FACT = "Tracker_reads_HttpContextAccessor_from_options_when_not_passed_directly"
EXPLICIT_FACT = "Tracker_explicit_accessor_takes_precedence_over_options"

# (name, what, file, old, new, occurrence (None = every one), test project, facts expected red)
MUTATIONS = [
    ("K1", "KafkaTracker ignores the options' accessor", KT, FALLBACK, NO_FALLBACK, None, "Kafka",
     [FALLBACK_FACT, "ProduceAsync_injects_the_requests_identity_when_the_options_carry_an_accessor"]),
    ("K2", "KafkaTracker prefers the options' accessor to the constructor's", KT, FALLBACK, SWAPPED, None, "Kafka", [EXPLICIT_FACT]),
    ("K3a", "the producer registration prefers the container's accessor", KDI, KDI_LINE, KDI_SWAP, 0, "Kafka", [f'{PREFERS}(registration: "producer")']),
    ("K3b", "the consumer registration prefers the container's accessor", KDI, KDI_LINE, KDI_SWAP, 1, "Kafka", [f'{PREFERS}(registration: "consumer")']),
    ("K3c", "the consumer factory registration prefers the container's accessor", KDI, KDI_LINE, KDI_SWAP, 2, "Kafka", [f'{PREFERS}(registration: "consumer factory")']),
    ("K3d", "the producer factory registration prefers the container's accessor", KDI, KDI_LINE, KDI_SWAP, 3, "Kafka", [f'{PREFERS}(registration: "producer factory")']),
    ("K4", "the registrations pass only the options' accessor", KDI, KDI_LINE, KDI_OPTS, None, "Kafka",
     [f'{CONTAINER}(registration: "{r}")' for r in ("producer", "consumer", "consumer factory", "producer factory")]),
    ("K5", "the producer stamps a message without the tracker's accessor", KP,
     "TestInfoResolver.ResolveWithSource(_tracker.HttpContextAccessor, _options.CurrentTestInfoFetcher)",
     "TestInfoResolver.ResolveWithSource(null, _options.CurrentTestInfoFetcher)", None, "Kafka",
     ["ProduceAsync_injects_the_requests_identity_when_the_options_carry_an_accessor",
      "Produce_injects_the_requests_identity_when_the_tracker_was_given_an_accessor"]),
    ("B1", "BigtableTracker ignores the options' accessor", BT, FALLBACK, NO_FALLBACK, None, "Bigtable", [FALLBACK_FACT]),
    ("B2", "BigtableTracker prefers the options' accessor to the constructor's", BT, FALLBACK, SWAPPED, None, "Bigtable", [EXPLICIT_FACT]),
    ("B3", "AddBigtableTestTracking prefers the container's accessor", BDI,
     "options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>()", "sp.GetService<IHttpContextAccessor>() ?? options.HttpContextAccessor",
     None, "Bigtable", [PREFERS]),
    ("B4", "AddBigtableTestTracking passes only the options' accessor", BDI,
     "options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>()", "options.HttpContextAccessor", None, "Bigtable", [CONTAINER]),
    ("S1", "SpannerTracker ignores the options' accessor", ST, FALLBACK, NO_FALLBACK, None, "Spanner",
     [FALLBACK_FACT, "WithTestTracking_on_a_DbConnection_passes_the_options_accessor_to_its_commands",
      "WithTestTracking_without_an_accessor_passes_the_options_accessor_to_its_tracker"]),
    ("S2", "SpannerTracker prefers the options' accessor to the constructor's", ST, FALLBACK, SWAPPED, None, "Spanner", [EXPLICIT_FACT]),
    ("S3", "AddSpannerTestTracking prefers the container's accessor", SDI,
     "options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>()", "sp.GetService<IHttpContextAccessor>() ?? options.HttpContextAccessor",
     None, "Spanner", [PREFERS]),
    ("S4", "AddSpannerTestTracking passes only the options' accessor", SDI,
     "options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>()", "options.HttpContextAccessor", None, "Spanner", [CONTAINER]),
    ("M1", "the proxy options drop the options' accessor", MX,
     "            TrackDuringAction = options.TrackDuringAction,\n            HttpContextAccessor = options.HttpContextAccessor\n",
     "            TrackDuringAction = options.TrackDuringAction\n", None, "MediatR",
     ["TrackMediatorForDiagrams_prefers_the_options_accessor_to_the_containers"]),
    ("M2", "the proxy prefers the container's accessor", MX,
     "reg.ProxyOptions.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>()",
     "sp.GetService<IHttpContextAccessor>() ?? reg.ProxyOptions.HttpContextAccessor", None, "MediatR",
     ["TrackMediatorForDiagrams_prefers_the_options_accessor_to_the_containers"]),
    ("M3", "the proxy takes only the options' accessor", MX,
     "reg.ProxyOptions.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>()", "reg.ProxyOptions.HttpContextAccessor", None, "MediatR",
     ["TrackMediatorForDiagrams_uses_the_containers_accessor_when_the_options_carry_none"]),
    ("E1", "SqlTrackingInterceptor ignores the options' accessor", EI, FALLBACK, NO_FALLBACK, None, "EfCore.Relational",
     ["Interceptor_reads_HttpContextAccessor_from_options_when_not_passed_directly",
      "WithSqlTestTracking_passes_the_options_accessor_to_its_interceptor"]),
    ("E2", "SqlTrackingInterceptor prefers the options' accessor to the constructor's", EI, FALLBACK, SWAPPED, None, "EfCore.Relational",
     ["Interceptor_explicit_accessor_takes_precedence_over_options"]),
    ("E3", "AddSqlTestTracking prefers the container's accessor", EDI,
     "options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>()", "sp.GetService<IHttpContextAccessor>() ?? options.HttpContextAccessor",
     None, "EfCore.Relational", ["AddSqlTestTracking_prefers_the_options_accessor_to_the_containers"]),
    ("E4", "AddSqlTestTracking passes only the options' accessor", EDI,
     "options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>()", "options.HttpContextAccessor", None, "EfCore.Relational",
     ["AddSqlTestTracking_uses_the_containers_accessor_when_the_options_carry_none"]),
    ("E5", "WithTestInfoFrom does not carry the accessor", EX,
     "        sqlOptions.HttpContextAccessor ??= httpOptions.HttpContextAccessor;\n", "", None, "EfCore.Relational",
     ["WithTestInfoFrom_CopiesHttpContextAccessor_when_the_sql_options_carry_none"]),
    ("E6", "WithTestInfoFrom overwrites the SQL options' own accessor", EX,
     "sqlOptions.HttpContextAccessor ??= httpOptions.HttpContextAccessor;", "sqlOptions.HttpContextAccessor = httpOptions.HttpContextAccessor;",
     None, "EfCore.Relational", ["WithTestInfoFrom_keeps_the_sql_options_own_HttpContextAccessor"]),
    ("G1", "an extension options type loses the property (the source guard)", BO,
     "    public Microsoft.AspNetCore.Http.IHttpContextAccessor? HttpContextAccessor { get; set; }\n", "", None, "guard",
     ["Every_extension_options_type_with_a_test_info_fetcher_has_an_HttpContextAccessor"]),
]


def run(project):
    if project == "guard":
        cmd = ["dotnet", "test", "tests/Kronikol.Tests/Kronikol.Tests.csproj", "-nologo", "-v", "q", "--no-build",
               "--filter", "FullyQualifiedName~ExtensionOptionsAccessorTests"]
    else:
        cmd = ["dotnet", "test", f"tests/Kronikol.Tests.{project}/Kronikol.Tests.{project}.csproj", "-nologo", "-v", "q"]
    out = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace").stdout
    failed = sorted(set(re.findall(r"\.(\w+(?:\(registration: \"[a-z ]+\"\))?) \[FAIL\]", out)))
    summary = next((l.strip() for l in out.splitlines() if "Failed!" in l or "Passed!" in l), "no summary: " + out[-300:].replace("\n", " "))
    return failed, summary


def mutate(text, old, new, occurrence):
    if occurrence is None:
        assert old in text
        return text.replace(old, new)
    parts = text.split(old)
    assert len(parts) > occurrence + 1, f"fewer than {occurrence + 1} occurrences"
    return old.join(parts[:occurrence + 1]) + new + old.join(parts[occurrence + 1:])


ok = True
for name, what, path, old, new, occurrence, project, expected in MUTATIONS:
    text = path.read_text(encoding="utf-8")
    path.write_text(mutate(text, old, new, occurrence), encoding="utf-8", newline="\n")
    try:
        failed, summary = run(project)
    finally:
        path.write_text(text, encoding="utf-8", newline="\n")
    caught = all(e in failed for e in expected)
    ok &= caught
    print(f"{name} ({what}): {'CAUGHT' if caught else 'MISSED'}", flush=True)
    print(f"  expected red: {', '.join(expected)}")
    print(f"  red: {', '.join(failed) or 'none'}")
    print(f"  {summary}", flush=True)

for project in ("Kafka", "Bigtable", "Spanner", "EfCore.Relational", "MediatR"):
    failed, summary = run(project)
    ok &= not failed
    print(f"Restored tree, {project}: {summary}")
sys.exit(0 if ok else 1)
