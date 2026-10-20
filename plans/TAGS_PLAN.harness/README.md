# TAGS_PLAN harness

`nunit-testcase-probe/` settles where NUnit 4.6.0 puts a parameterized method's own attributes (F14 in `../TAGS_PLAN.md`).
It builds NUnit's own test tree with `DefaultTestAssemblyBuilder`, without running a test, and prints each node's own
properties, its `Parent` and `AllCategories()`. Copy the folder outside the repository (so the repository's
`Directory.Build.props` does not apply) and run `dotnet run -c Release`. `results.txt` is the output of 2026-10-19.

The result: a `[TestCase]` method's `[Category]` and `[Property]` attributes sit on its `ParameterizedMethodSuite`, not on
each case, and a case's `Parent` is that suite, not the fixture. `[TestCase(Category = …)]` lands on the case itself.
`AllCategories()` returns the case's, the method's and the fixture's categories, in that order.

`case-fold/` compares how .NET (`fold.ps1`), JavaScript (`fold.js`) and Java (`Fold.java`) fold the case of two
characters outside ASCII, the Kelvin sign and the dotless ı. They disagree, which is why §4.3 folds ASCII letters only.
`results.txt` is the output of 2026-10-19.
