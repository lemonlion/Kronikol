# SERVICE_CARD_PLAN harness

Probes and results behind `plans/SERVICE_CARD_PLAN.md` (#191), measured on 2026-10-26 against the 5.11.0 tool built
from `44bcc62a`.

## What is here

| Path | What it is |
|---|---|
| `probes/ledger_stability.py` | Compares consecutive run lines of one suite on one commit in a history ledger: how often counts, call sets, call order, the run's distinct operations and its edges change between two runs of the same code (plan 2.4) |
| `probes/card-prototype.cs` | Builds the card's behaviour from a decoded data file: `CallNesting`'s rule ported, the product's own `InteractionShape.Template` and `TemplateStatement`, a prototype inference of the test client and the subject. Prints the origin split, the service's calls outside any received call, operation counts and the card's size three ways (plan 2.3) |
| `inputs/examples-5.8.0/` | The three Example.Api component reports (`TestRunReport.krkl` and `Run.json`), written by 5.8.0 |
| `results/ledger-stability-breakfastprovider.txt` | `ledger_stability.py` over BreakfastProvider's ledger |
| `results/card-prototype.txt` | `card-prototype.cs` over four BreakfastProvider lanes and the Example.Api Reqnroll report |

## What is not here, and how to get it again

BreakfastProvider's data files and ledger are not committed (several MB each; the ledger is 19 MB). To fetch them:

```bash
# the four lanes of CI run 40290999167 (cf47045, Kronikol 5.4.1); report artifacts expire, so a later run may be needed
for a in xunit-in-memory-report reqnroll-in-memory-report xunit-external-sut-report reqnroll-in-docker-report; do
  gh run download 40290999167 -R lemonlion/BreakfastProvider -n $a -D bp/$a
done
# the ledger
gh api "repos/lemonlion/BreakfastProvider/contents/history.jsonl?ref=kronikol-history" \
  -H "Accept: application/vnd.github.raw" > history.jsonl
```

Then decode each lane and run the probes:

```bash
kronikol decode bp/xunit-in-memory-report -o decoded/xunit-in-memory.json
dotnet run --file probes/card-prototype.cs -- decoded/xunit-in-memory.json cards/xunit-in-memory.card.json
python -I -X utf8 probes/ledger_stability.py history.jsonl
```

`card-prototype.cs` references `src/Kronikol/Kronikol.csproj` by a relative path, so it runs from this folder in a
checkout. Python's `-I` ignores `PYTHONIOENCODING`; `-X utf8` is what lets it print the ledger's arrows on Windows.

## Peers' scripts cited by the plan

Measured by the sessions writing the sibling plans, on the same decoded files, and cited as PEER:

- kronikol-67 (#190): `parties.py`, `infer.py`, `hosts.py`, in its session scratchpad; they move into
  `PARTY_IDENTITY_PLAN.harness/` with that plan.
- kronikol-bf (#192): the call-identity rules and the GraphQL counts, in `CALL_OPERATION_AND_ORIGIN_PLAN.harness/`.
