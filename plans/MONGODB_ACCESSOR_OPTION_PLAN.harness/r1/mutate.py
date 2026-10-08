# R1's mutations (plan section 5): each breaks one line of the fix, runs the MongoDB tests (driver 3.x project), and
# records which facts fail. Run from the repository root with a MongoDB in KRONIKOL_TEST_MONGO (start-mongo.sh):
#   KRONIKOL_TEST_MONGO=mongodb://127.0.0.1:27017 python plans/MONGODB_ACCESSOR_OPTION_PLAN.harness/r1/mutate.py
import pathlib, re, subprocess, sys

SRC = pathlib.Path("src/Kronikol.Extensions.MongoDB")
SUB, EXT, DI = SRC / "MongoDbTrackingSubscriber.cs", SRC / "MongoClientSettingsExtensions.cs", SRC / "MongoDbServiceCollectionExtensions.cs"
DI_LINE = "options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>()"

MUTATIONS = [
    ("M1", "the subscriber ignores the options' accessor", SUB,
     "_httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;", "_httpContextAccessor = httpContextAccessor;",
     ["Subscriber_reads_HttpContextAccessor_from_options_when_not_passed_directly",
      "WithTestTracking_passes_the_options_accessor_to_its_subscriber",
      "A_hosts_commands_land_in_the_scenario_its_request_names"]),
    ("M2", "the options' accessor outranks the constructor's", SUB,
     "_httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;", "_httpContextAccessor = options.HttpContextAccessor ?? httpContextAccessor;",
     ["Subscriber_explicit_accessor_takes_precedence_over_options"]),
    ("M3", "WithTestTracking drops the earlier configurator", EXT,
     "existingConfigurator?.Invoke(builder);", "",
     ["WithTestTracking_PreservesExistingClusterConfigurator"]),
    ("M4", "AddMongoDbTestTracking prefers the container's accessor", DI,
     DI_LINE, "sp.GetService<IHttpContextAccessor>() ?? options.HttpContextAccessor",
     ["AddMongoDbTestTracking_prefers_the_options_accessor_to_the_containers"]),
    ("M5", "AddMongoDbTestTracking passes only the options' accessor", DI,
     DI_LINE, "options.HttpContextAccessor",
     ["AddMongoDbTestTracking_uses_the_containers_accessor_when_the_options_carry_none"]),
    ("M6", "WithTestTracking never subscribes its subscriber", EXT,
     "subscriber.Subscribe(builder);", "",
     ["A_hosts_commands_land_in_the_scenario_its_request_names"]),
]

def run_tests():
    out = subprocess.run(["dotnet", "test", "tests/Kronikol.Tests.MongoDB/Kronikol.Tests.MongoDB.csproj", "-nologo", "-v", "q"],
                         stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace").stdout
    failed = sorted(set(re.findall(r"\.(\w+) \[FAIL\]", out)))
    summary = next((l.strip() for l in out.splitlines() if "Failed!" in l or "Passed!" in l), out[-400:])
    return failed, summary

ok = True
for name, what, path, old, new, expected in MUTATIONS:
    text = path.read_text(encoding="utf-8")
    assert text.count(old) == 1, f"{name}: the line to mutate is not in {path} exactly once"
    path.write_text(text.replace(old, new), encoding="utf-8", newline="\n")
    try:
        failed, summary = run_tests()
    finally:
        path.write_text(text, encoding="utf-8", newline="\n")
    caught = all(f in failed for f in expected)
    ok &= caught
    print(f"{name} ({what}): {'CAUGHT' if caught else 'MISSED'}")
    print(f"  expected red: {', '.join(expected)}")
    print(f"  red: {', '.join(failed) or 'none'}")
    print(f"  {summary}")

failed, summary = run_tests()
print(f"Restored tree: {summary}")
sys.exit(0 if ok and not failed else 1)
