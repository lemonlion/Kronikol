# The floor ledger's facts against one change each that they exist to catch (plans/CLIENT_FLOORS_PLAN.md 5.2): each
# mutation is applied, FloorLedgerTests runs, the facts that failed are recorded, and the file is put back.
#   python -I ledger_mutations.py <worktree> <out> <logs>
import os, re, subprocess, sys

W, OUT, LOGS = sys.argv[1:4]
os.makedirs(LOGS, exist_ok=True)
LEDGER = "tests/Kronikol.Tests/Packaging/FloorLedger.cs"
MUTATIONS = [
    ("a measured package floats again", "src/Kronikol.Extensions.Kafka/Kronikol.Extensions.Kafka.csproj",
     '<PackageReference Include="Confluent.Kafka" Version="2.2.0" />', '<PackageReference Include="Confluent.Kafka" Version="2.*" />'),
    ("a floor moves without the ledger", "src/Kronikol.Extensions.PubSub/Kronikol.Extensions.PubSub.csproj",
     '<PackageReference Include="Google.Cloud.PubSub.V1" Version="3.35.0" />', '<PackageReference Include="Google.Cloud.PubSub.V1" Version="3.36.0" />'),
    ("a new package floats unlisted", "src/Kronikol.Extensions.Grpc/Kronikol.Extensions.Grpc.csproj",
     '<PackageReference Include="Google.Protobuf" Version="3.19.4" />',
     '<PackageReference Include="Google.Protobuf" Version="3.19.4" />\n    <PackageReference Include="Grpc.Core.Api" Version="2.*" />'),
    ("the still-floating count is raised past the marks", LEDGER,
     "internal const int StillFloatingAtMost = 16;", "internal const int StillFloatingAtMost = 15;"),
    ("an entry outlives its reference", LEDGER,
     'Measured("Kronikol.Extensions.Bigtable", "Google.Cloud.Bigtable.V2", "3.0.0", "R2a: floor/r2a-bigtable.txt"),',
     'Measured("Kronikol.Extensions.Bigtable", "Google.Cloud.Bigtable.V2", "3.0.0", "R2a: floor/r2a-bigtable.txt"),\n'
     '        Measured("Kronikol.Extensions.Bigtable", "Google.Cloud.Bigtable.Admin.V2", "3.0.0", "R2a: floor/r2a-bigtable.txt"),'),
    ("a package listed twice", LEDGER,
     'Measured("Kronikol.Extensions.Kafka", "Confluent.Kafka", "2.2.0", "R2a: floor/r2a-kafka.txt"),',
     'Measured("Kronikol.Extensions.Kafka", "Confluent.Kafka", "2.2.0", "R2a: floor/r2a-kafka.txt"),\n'
     '        Measured("Kronikol.Extensions.Kafka", "Confluent.Kafka", "2.2.0", "R2a: floor/r2a-kafka.txt"),'),
    ("a measured entry without evidence", LEDGER,
     'Measured("Kronikol.Extensions.Kafka", "Confluent.Kafka", "2.2.0", "R2a: floor/r2a-kafka.txt")',
     'Measured("Kronikol.Extensions.Kafka", "Confluent.Kafka", "2.2.0", "")'),
]


def test(name):
    log = os.path.join(LOGS, re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-") + ".log")
    with open(log, "w", encoding="utf-8") as f:
        code = subprocess.run(["dotnet", "test", os.path.join(W, "tests/Kronikol.Tests"), "-nologo",
                               "--filter", "FullyQualifiedName~Kronikol.Tests.Packaging.FloorLedgerTests"],
                              stdout=f, stderr=subprocess.STDOUT).returncode
    out = open(log, encoding="utf-8", errors="replace").read()
    failed = sorted(set(re.findall(r"FloorLedgerTests\.(\w+) \[FAIL\]", out)))
    summary = " ".join(re.findall(r"(?:Passed|Failed)!\s+-\s+Failed:\s+\d+,\s+Passed:\s+\d+", out)) or "no summary"
    return code, failed, re.sub(r"\s+", " ", summary)


with open(OUT, "w", encoding="utf-8", newline="\n") as o:
    code, failed, summary = test("unchanged")
    o.write(f"unchanged | exit {code} | {summary} | failed: {', '.join(failed) or 'none'}\n")
    for name, rel, old, new in MUTATIONS:
        path = os.path.join(W, rel)
        original = open(path, encoding="utf-8", newline="").read()
        assert original.count(old) == 1, (name, old)
        try:
            open(path, "w", encoding="utf-8", newline="").write(original.replace(old, new))
            code, failed, summary = test(name)
        finally:
            open(path, "w", encoding="utf-8", newline="").write(original)
        o.write(f"{name} | exit {code} | {summary} | failed: {', '.join(failed) or 'NONE (the mutation survived)'}\n")
        o.flush()
        print(name, failed, flush=True)
