# R3: every template's sample test, as a user runs it

Each template packed from this branch, installed into a private hive, created with `dotnet new <template> -n ScaffoldSmoke`
and its sample test run as a user would, outside a solution (`nosln`) and inside a new one (`withsln`): `dotnet test`, or
`dotnet run` for the TUnit templates. Script: the session's `tpl_measure.sh`; `<tmp>` stands for its scratch directory.

- `before/`: the templates as published at 4.13.3, measured 2026-10-08. 21 of 24 runs failed, for three causes: the test
  framework's entry point instead of the placeholder's `Main` (CS8892, "the entry point exited without ever building an
  IHost"), no content root outside a solution ("Solution root could not be located"; LightBDD's TUnit template reports it
  as "LightBddScopeAttribute is not defined"), and no `Microsoft.NET.Test.Sdk` in the BDDfy and ReqNRoll xUnit v3 templates
  ("testhost.dll was not found"). The TUnit templates built with an ASPDEPR004 warning.
- `after/`: this branch. 24 of 24 passed, with no CS8892 or ASPDEPR004 warning.
- `red-proofs.txt`: `TemplateTestHostTests` on this branch and with 4.14.2's templates in its place.
