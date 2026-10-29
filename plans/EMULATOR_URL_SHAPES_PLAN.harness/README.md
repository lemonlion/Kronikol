# Harness: EMULATOR_URL_SHAPES_PLAN.md (#125)

The probes behind every RUN line of [`../EMULATOR_URL_SHAPES_PLAN.md`](../EMULATOR_URL_SHAPES_PLAN.md), with the
output each one printed on 2026-10-27. Each probe is a file-based app: `dotnet run --file probes/<name>.cs` with the
.NET 10 SDK (measured on 10.0.300, Windows 11). They take the **published 5.12.0** Kronikol packages from nuget.org,
never the working tree, so they measure what a consumer has, and they build nothing in the repository. Three were
added when R1 was built, on 2026-10-29: the two `before9-record-*` probes take the **published 5.27.2** packages, the
last before the fix, and `bigquery-emulator-live-r1.cs` builds the BigQuery extension from this checkout's source.

Every probe but one puts the shipped tracking handler between the real SDK client and a stub server, so each URL in
the output is one the SDK wrote. The handlers log a request before calling their inner handler, so the stub sees,
request by request, whether the call it is answering was recorded and what as.

| Probe | What it measures | Output |
|---|---|---|
| `probes/bigquery-url-shapes.cs` | `BigQueryClient` (Google.Cloud.BigQuery.V2 3.12.0), 25 operations, over five `BaseUri` shapes: the default, an emulator's root with and without the slash, `…/bigquery/v2/`, and a gateway prefix `/bq/`. What the classifier reads, what `Detailed` records, whether `Summarised` records it | `results/bigquery-url-shapes.txt` |
| `probes/bigquery-emulator-live.cs` | The issue's repro, re-run against the real emulator `ghcr.io/goccy/bigquery-emulator:0.8.1` (project `probe`): a root base and a `/bigquery/v2/` base, at `Detailed` and `Summarised`. Takes the emulator's URL as its argument | `results/bigquery-emulator-live.txt` |
| `probes/sibling-url-shapes.cs` | The same question for Blob Storage (Azure.Storage.Blobs 12.30.1) and Storage Queues (Azure.Storage.Queues 12.28.1) with a production connection string and `UseDevelopmentStorage=true` (Azurite), and for Cloud Storage (Google.Cloud.Storage.V1 4.16.0) with `BaseUri` default, `…/storage/v1/` and root | `results/sibling-url-shapes.txt` |
| `probes/azure-uri-builders.cs` | What the Azure SDK's own `BlobUriBuilder` and `QueueUriBuilder` make of each Azurite host shape: an IP on the emulator's port or a random one, a docker compose name on the emulator's port, `localhost` on a random port | `results/azure-uri-builders.txt` |
| `probes/api-surface.cs` | Every method the generated clients declare (Google.Apis.Bigquery.v2 1.74.0.4090: 47; Google.Apis.Storage.v1 1.74.0.4115: 82), read by reflection from each request class's `RestPath` and `HttpMethod`, filled with sample values and classified under Google's base and an emulator's root. Run twice: bucket `orders` (the default) and `BUCKET=archive`, which separates F12's substring bug from the rest | `results/api-surface.txt`, `results/api-surface-bucket-archive.txt` |
| `probes/gcs-copy-object.cs` | Which request `StorageClient.CopyObject` sends (`rewriteTo`), and what each verbosity records | `results/gcs-copy-object.txt` |
| `probes/storage-emulator-env.cs` | The third way a Cloud Storage client meets an emulator: `STORAGE_EMULATOR_HOST`, read by `StorageClientBuilder.EmulatorDetection`, with four spellings of the host. The base the client ends up with, and what each request records at `Detailed` (added at 5.14.0; the handler's source is unchanged since 5.12.0) | `results/storage-emulator-env.txt` |
| `probes/before9-record-bigquery.cs` | What the published 5.27.2 BigQuery package records for each request `BigQueryClient` 3.12.0 sends under the five bases of `bigquery-url-shapes.cs`, and for the methods it misread on Google's endpoint, at `Detailed` and `Summarised`: 248 requests. Copied verbatim into `tests/Kronikol.Tests.BigQuery/RecordedBefore9Record.cs`, the record the frozen classifier and rules 6 to 8 are pinned to | `results/before9-record-bigquery.tsv` |
| `probes/before9-record-cloudstorage.cs` | The same for the published 5.27.2 Cloud Storage package and `StorageClient` 4.16.0 under three bases: 104 requests, copied into `tests/Kronikol.Tests.CloudStorage/RecordedBefore9Record.cs` | `results/before9-record-cloudstorage.tsv` |
| `probes/bigquery-emulator-live-r1.cs` | `bigquery-emulator-live.cs` again, against R1's build (#125 fixed): the same emulator, bases and verbosities, and the responses the handler logged for the sends that threw (F16, Q5) | `results/bigquery-emulator-live-r1.txt` |

## Reading the output

- `requests sent N; Summarised recorded M` (or `recorded M`) counts the calls that reached the stub and the ones the
  handler logged.
- `NOT RECORDED` / `(not recorded)`: the handler sent the call on without a record, which is `Summarised`'s skip of
  `Other`.
- In `bigquery-emulator-live.txt` a run's `recorded` can exceed `sent`: the emulator answers a resumable upload with a
  session URL on its own bind address (`http://0.0.0.0:9050/…`), the client's `PUT`s to it throw, and the handler had
  already logged each one (F16). The recorder counts only requests that got an answer.

## Artifacts of the stub, not findings

- `sibling-url-shapes.txt`, Cloud Storage `UploadObject[3]`: a `DELETE …?generation=1` after the upload. The stub's
  object carries no hashes, so the client's upload validation deletes what it uploaded. A real server sends hashes.
- Some operations throw after their request (a response body the client could not use). The request was already sent
  and recorded, which is all the probes measure.

## The emulator

Run in the `podman-machine-default` WSL distro (podman as root), on a port of its own beside another session's
containers:

```sh
MSYS_NO_PATHCONV=1 wsl -d podman-machine-default -u root -- podman run -d --name bq125 -p 19070:9050 ghcr.io/goccy/bigquery-emulator:0.8.1 --project=probe
dotnet run --file probes/bigquery-emulator-live.cs -- http://127.0.0.1:19070
MSYS_NO_PATHCONV=1 wsl -d podman-machine-default -u root -- podman rm -f bq125
```

## Measured elsewhere

The 3.11.0 and 3.12.0 request paths of `ExecuteQuery` were measured by the session planning #205; its harness is
cited in the plan's §12.
