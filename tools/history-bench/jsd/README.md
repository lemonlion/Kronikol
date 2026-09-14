# jsd — running the report's own export function (§8.1)

`npm install && node export.js` (from this directory). Loads the shipped
`src/Kronikol/Reports/report-export-function.js` into a jsdom document shaped like a real report, and
captures what `export_html()` hands to the download by stubbing `Blob`/`URL` — **the function under
test is not modified**.

| Assertion | Result |
|---|---|
| `#history-data` as a **direct child of `<body>`** is copied, payload intact | ✅ |
| **the same script nested inside a `<div>` is NOT copied** | ⚠️ `:scope > script` is direct-child-only |
| `#puml-data` pruned to the ids the filtered export contains | ✅ |
| hidden features excluded | ✅ |
| the aggregate History `<section>` does not survive | ✅ (intended) |
| `<head>` carried over whole | ✅ |

Two requirements for M5 fall out: the history payload must be a **direct child of `<body>`** (a
tidying wrapper would silently break export), and the history render script must **tolerate a missing
History section**, because head scripts are copied and will run against exactly that document.
