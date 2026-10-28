| Scenario | 0.4 / 3.11.0 | 0.4 / 3.12.0 | 0.6.0 / 3.11.0 | 0.6.0 / 3.12.0 | 0.6.1 / 3.11.0 | 0.6.1 / 3.12.0 | 0.6.6 / 3.11.0 | 0.6.6 / 3.12.0 | 0.8.1 / 3.0.0 | 0.8.1 / 3.11.0 | 0.8.1 / 3.12.0 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Q1 ExecuteQueryAsync(sql, null, QueryOptions { UseQueryCache = false }), the issue's call | ok | FormatException `1698364800.0` | ok | FormatException `1698364800.0` | ok | ok | ok | ok | ok | ok | ok |
| Q2 ExecuteQueryAsync(sql, null), no options | ok | FormatException `1698364800.0` | ok | FormatException `1698364800.0` | ok | ok | ok | ok | ok | ok | ok |
| Q3 a TIMESTAMP literal, no table | ok | FormatException `1698364800.0` | ok | FormatException `1698364800.0` | ok | ok | ok | ok | ok | ok | ok |
| Q4 a TIMESTAMP query parameter | job failed | API 400 | job failed | API 400 | job failed | API 400 | job failed | API 400 | string, not a time | string, not a time | string, not a time |
| Q5 RECORD and REPEATED timestamps | API 500 | FormatException `2023-10-27T00:00:01Z` | API 400 | FormatException `2023-10-27T00:00:01Z` | API 400 | API 500 | API 400 | API 500 | FormatException `2023-10-27 00:00:01.500000+00` | FormatException `2023-10-27 00:00:01.500000+00` | FormatException `2023-10-27 00:00:01.500000+00` |
| Q6 ListRows (tabledata.list) | FormatException `1698364800.0` | FormatException `1698364800.0` | FormatException `1698364800.0` | FormatException `1698364800.0` | API 500 | API 500 | API 500 | API 500 | top-level ok, nested FormatException `2023-10-27 00:00:01.500000+00` | top-level ok, nested FormatException `2023-10-27 00:00:01.500000+00` | top-level ok, nested FormatException `2023-10-27 00:00:01.500000+00` |
| Q7 DML INSERT, then SELECT the inserted row | ok | FormatException `1698364801.0` | ok | FormatException `1698364801.0` | ok | ok | ok | ok | ok | ok | ok |
| Q8 QueryOptions { Priority = Interactive } (the default priority, set explicitly) | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| Q9 QueryOptions { JobIdPrefix } | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| Q10 GetQueryResultsOptions { UseInt64Timestamp = false } | ok | ok | ok | ok | ok | ok | ok | ok | n/a | ok | ok |
| Q11 CreateQueryJob + GetQueryResults (the job path, by hand) | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
