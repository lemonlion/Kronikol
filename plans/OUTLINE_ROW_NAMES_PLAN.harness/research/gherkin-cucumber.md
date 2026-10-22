# Gherkin's outline-row names, Kronikol's Cucumber lane, and Kronikol4J

Research for [`../../OUTLINE_ROW_NAMES_PLAN.md`](../../OUTLINE_ROW_NAMES_PLAN.md) (#101), 2026-10-22. Labels as in the
plan: **RUN** (executed for this file; the output is in Appendix A), **READ** (in the source named, at the file:line
given), **INFERRED** (reasoned from what was read or run, and shown by neither on its own).

Sources, each read at a fixed commit:

- cucumber/gherkin, .NET: `dotnet/Gherkin/CucumberMessages/Pickles/PickleCompiler.cs` at
  `9bdaf5bea5ad4ac0a2f27c139fcf5468c608d75e` (the commit in the brief) and at `1adafde16942e8a0ea1e0b3223f9f6d12cb4bae1`
  (Gherkin 35.0.0, the version Reqnroll 3.3.4 uses); `GherkinLine.cs`, `TokenMatcher.cs`,
  `CucumberMessages/AstMessagesConverter.cs` and `CucumberMessages/CucumberMessagesDefaults.cs` at 35.0.0.
- cucumber/gherkin, Java: `java/src/main/java/io/cucumber/gherkin/PickleCompiler.java`, `TableRowLine.java` and
  `StringUtils.java` at 9bdaf5b, and `PickleCompiler.java` at tag `v28.0.0`. JavaScript: `javascript/src/pickles/compile.ts`
  at 9bdaf5b.
- reqnroll/Reqnroll: `Reqnroll.Parser/` at `33eeea2298e62d53355ecc36e487b1fcbc0d9270` (the commit in the Reqnroll 3.3.4
  nuspec).
- cucumber/cucumber-jvm at tag `v7.18.1` (the version Kronikol4J compiles against).
- Kronikol at `C:/Code/Kronikol-101` (`eabec2bd`, 5.4.0); Kronikol4J at `C:/Code/Kronikol4J` (local `main` at `92d6b28`,
  the ledger at `origin/main` `b6a5dac`, see 3.3).

Every file was fetched with curl from raw.githubusercontent.com. WebFetch was tried first and returned a summary rather
than the text, so no quote below comes from it. Copies are in the scratch folder listed at the end.

## 0. The answers in brief

| Question | Answer | Label |
|---|---|---|
| Gherkin version Reqnroll 3.3.4 uses | 35.0.0, at run time and in the build-time generator (a byte-identical DLL) | READ |
| Is Reqnroll's pickle name Gherkin's? | yes: Reqnroll compiles its pickles with Gherkin's own `PickleCompiler` | READ |
| Order of replacement | header columns left to right; each column's replace runs over the string the one before returned | READ, RUN |
| Placeholder with no column | left as written | READ, RUN |
| Value holding `<another header>` | filled again if that header's column is to the right; left as written if it is to the left, or is its own | READ, RUN |
| Duplicate headers | Gherkin parses them and the leftmost column wins; Reqnroll refuses the feature at generation | READ, RUN |
| Empty value | the placeholder is replaced with nothing | READ, RUN |
| Header trimmed? | by the parser (space, tab and U+00A0 at either end), never by `Interpolate`; inner spaces kept | READ, RUN |
| Case | ordinal, case-sensitive | READ, RUN |
| Parser's unescaped cell values? | yes, for headers and values alike (section 1.4) | READ, RUN |
| Java and JavaScript | the same rule | READ |
| Cucumber lane, DisplayName | the pickle's name, so the filled title | READ |
| Cucumber lane, raw title | OutlineId always; DisplayName only when the pickle's name is empty | READ, INFERRED |
| Two rows, one filled name | two scenarios with one name; nothing renames or merges them | READ |
| Kronikol4J | names a row `testCase.getName()`, the pickle's filled name; no outline id or example values; no lane gets a raw Gherkin title | READ |

## 1. Gherkin's rule

### 1.1 Which Gherkin, and that Reqnroll's names come from it

- **READ** `C:/Users/cex/.nuget/packages/reqnroll/3.3.4/reqnroll.nuspec` lists `<dependency id="Gherkin" version="35.0.0"
  exclude="Build,Analyzers" />`, and its repository commit is `33eeea2298e62d53355ecc36e487b1fcbc0d9270`. Kronikol-101
  references `Reqnroll` 3.3.4 (`src/Kronikol.ReqNRoll.Core/Kronikol.ReqNRoll.Core.csproj:12`, and the examples and
  templates alike) and no Gherkin package of its own: no `*.csproj`, `*.props` or `*.targets` in the checkout names one.
  The cache also holds Gherkin 42.0.1, which nothing in Kronikol-101 references.
- **READ** `C:/Users/cex/.nuget/packages/gherkin/35.0.0/gherkin.nuspec` gives the repository commit
  `1adafde16942e8a0ea1e0b3223f9f6d12cb4bae1`; the assembly says `35.0.0+1adafde…` (RUN, Appendix A.3, first line).
- **READ** The build-time generator's copy,
  `reqnroll.tools.msbuild.generation/3.3.4/build/netstandard2.0/Gherkin.dll` (and `build/net462/Gherkin.dll`), is
  byte-identical to `gherkin/35.0.0/lib/netstandard2.0/Gherkin.dll`: sha256
  `9e18ba03bbcaa32b9d2fd9cdbfc1ff9cfe55bb873bd8d84a51a3f36b2cc5ec3d` for all three.
- **READ** Reqnroll compiles pickles with Gherkin's compiler, `Reqnroll.Parser/CucumberMessages/CucumberMessagesConverter.cs:65-69`
  at 33eeea2 (the class summary, lines 8-12, says it "uses two classes from the Gherkin project: AstMessagesConverter and
  PickleCompiler"):

```csharp
    public IEnumerable<Pickle> ConvertToCucumberMessagesPickles(GherkinDocument gherkinDocument)
    {
        var pickleCompiler = new Gherkin.CucumberMessages.Pickles.PickleCompiler(_idGenerator);
        return pickleCompiler.Compile(gherkinDocument);
    }
```

  So the pickles Reqnroll embeds in a feature's messages carry exactly the names this section describes. That the
  generator calls this method at build time to make the embedded messages is **INFERRED** (the summary above, and
  `Reqnroll.Parser.dll` references `PickleCompiler`); the call chain was not traced.
- **READ** At the brief's commit 9bdaf5b, `PickleCompiler.cs` is byte-identical to Gherkin 42.0.1's (commit
  `33693f4b5a76bb86dee61d0b287d6f2c700813e4`; sha256 `5c1cdda36ca42ce4a13bb5ab54c8bdb351bf2869ed20e3f2e5273823fb1236d4` for
  both). It differs from 35.0.0's in two places, neither in the rule: the pickle carries a `Location` (`scenario.Location`,
  `values.Location`), and a step may have a doc string and a data table together (`CreatePickleArgument`, the two
  numbered by line order). `Interpolate` and the places that call it with a row's cells are the same.

### 1.2 `Interpolate`

Gherkin 35.0.0, what Reqnroll 3.3.4 runs (`PickleCompiler.cs:256-267` at 1adafde):

```csharp
    protected virtual string Interpolate(string name, IEnumerable<TableCell> variableCells, IEnumerable<TableCell> valueCells)
    {
        int col = 0;
        foreach (var variableCell in variableCells)
        {
            var valueCell = valueCells.ElementAt(col++);
            string header = variableCell.Value;
            string value = valueCell.Value;
            name = name.Replace("<" + header + ">", value);
        }
        return name;
    }
```

At 9bdaf5b (`PickleCompiler.cs:277-288`), the same text:

```csharp
    protected virtual string Interpolate(string name, IEnumerable<TableCell> variableCells, IEnumerable<TableCell> valueCells)
    {
        int col = 0;
        foreach (var variableCell in variableCells)
        {
            var valueCell = valueCells.ElementAt(col++);
            string header = variableCell.Value;
            string value = valueCell.Value;
            name = name.Replace("<" + header + ">", value);
        }
        return name;
    }
```

### 1.3 Where it is called

The pickle's name and its steps, 35.0.0 `PickleCompiler.cs:119-166` (`CompileScenarioOutline`; name at `:156`, step text
at `:142`, the step's argument at `:147`):

```csharp
    protected virtual void CompileScenarioOutline(List<Pickle> pickles,
        Func<IEnumerable<PickleStep>> backgroundStepsFactory, Scenario scenarioOutline,
        IEnumerable<Tag> featureTags, string language, string gherkinDocumentUri)
    {
        foreach (var examples in scenarioOutline.Examples)
        {
            if (examples.TableHeader == null) continue;
            var variableCells = examples.TableHeader.Cells;
            foreach (var values in examples.TableBody)
            {
                var valueCells = values.Cells;

                var steps = new List<PickleStep>();
                if (scenarioOutline.Steps.Any())
                    steps.AddRange(backgroundStepsFactory());

                var tags = new List<Tag>();
                tags.AddRange(featureTags);
                tags.AddRange(scenarioOutline.Tags);
                tags.AddRange(examples.Tags);

                foreach (var scenarioOutlineStep in scenarioOutline.Steps)
                {
                    string stepText = Interpolate(scenarioOutlineStep.Text, variableCells, valueCells);

                    PickleStep pickleStep = CreatePickleStep(
                            scenarioOutlineStep,
                            stepText,
                            CreatePickleArgument(scenarioOutlineStep, variableCells, valueCells),
                            new[] { scenarioOutlineStep.Id, values.Id }
                    );
                    steps.Add(pickleStep);
                }

                Pickle pickle = new Pickle(
                        idGenerator.GetNewId(),
                        gherkinDocumentUri,
                        Interpolate(scenarioOutline.Name, variableCells, valueCells),
                        language,
                        steps,
                        PickleTags(tags),
                        new List<string> { scenarioOutline.Id, values.Id }
                );

                pickles.Add(pickle);
            }
        }
    }
```

Doc strings and data tables, 35.0.0 `PickleCompiler.cs:189-234` (every cell of the step's table, its first row included;
the doc string's media type and content):

```csharp
    protected virtual PickleStepArgument CreatePickleArgument(Step argument)
    {
        var noCells = Enumerable.Empty<TableCell>();
        return CreatePickleArgument(argument, noCells, noCells);
    }

    protected virtual PickleStepArgument CreatePickleArgument(Step step, IEnumerable<TableCell> variableCells, IEnumerable<TableCell> valueCells)
    {
        if (step.DataTable != null)
        {
            var t = step.DataTable;
            var rows = t.Rows;
            var newRows = new List<PickleTableRow>(rows.Count());
            foreach (var row in rows)
            {
                var cells = row.Cells;
                var newCells = new List<PickleTableCell>();
                foreach (var cell in cells)
                {
                    newCells.Add(
                            new PickleTableCell(
                                    Interpolate(cell.Value, variableCells, valueCells)
                            )
                    );
                }
                newRows.Add(new PickleTableRow(newCells));
            }
            return new PickleStepArgument(null, new PickleTable(newRows));
        }

        if (step.DocString != null)
        {
            var ds = step.DocString;
            return
                new PickleStepArgument
                (
                    new PickleDocString(
                        ds.MediaType == null ? null : Interpolate(ds.MediaType, variableCells, valueCells),
                        Interpolate(ds.Content, variableCells, valueCells)
                    ),
                    null
                );
        }

        return null;
    }
```

The same at 9bdaf5b, moved into `CreatePickleDocString` and `CreatePickleTable` (`PickleCompiler.cs:227-255`):

```csharp
    protected virtual PickleDocString CreatePickleDocString(Nullable<long> argumentIndex, DocString docString, IEnumerable<TableCell> variableCells, IEnumerable<TableCell> valueCells)
    {
        return new PickleDocString(
            argumentIndex,
            docString.MediaType == null ? null : Interpolate(docString.MediaType, variableCells, valueCells),
            Interpolate(docString.Content, variableCells, valueCells)
        );
    }

    protected virtual PickleTable CreatePickleTable(Nullable<long> argumentIndex, DataTable dataTable, IEnumerable<TableCell> variableCells, IEnumerable<TableCell> valueCells)
    {
        var rows = dataTable.Rows;
        var newRows = new List<PickleTableRow>(rows.Count());
        foreach (var row in rows)
        {
            var cells = row.Cells;
            var newCells = new List<PickleTableCell>();
            foreach (var cell in cells)
            {
                newCells.Add(
                        new PickleTableCell(
                                Interpolate(cell.Value, variableCells, valueCells)
                        )
                );
            }
            newRows.Add(new PickleTableRow(newCells));
        }
        return new PickleTable(argumentIndex, newRows);
    }
```

What is never filled (**READ**, 35.0.0 line numbers):

- A scenario without Examples, whatever its keyword. `BuildScenario` (`:80-91`) sends it to `CompileScenario`, whose
  pickle is named `scenario.Name` as written (`:110`), so a `Scenario Outline:` with no `Examples:` keeps its
  placeholders. The other way round, `Scenario:` followed by `Examples:` is compiled as an outline: only
  `scenario.Examples.Any()` decides (`:83`).

```csharp
    private void BuildScenario(List<Pickle> pickles, string language, IEnumerable<Tag> tags, Func<IEnumerable<PickleStep>> backgroundStepsFactory, string gherkinDocumentUri, Scenario scenario)
    {
        CurrentStepKeywordType = PickleStepType.UNKNOWN;
        if (!scenario.Examples.Any())
        {
            CompileScenario(pickles, backgroundStepsFactory, scenario, tags, language, gherkinDocumentUri);
        }
        else
        {
            CompileScenarioOutline(pickles, backgroundStepsFactory, scenario, tags, language, gherkinDocumentUri);
        }
    }
```

- Background steps: `PickleSteps` and `PickleStep` (`:236-254`) call `CreatePickleArgument(step)`, which passes no cells
  (`:189-193`), and use `step.Text` as it is (`:250`).
- An Examples block with no header row is skipped (`:125`); one with a header and no body rows makes no pickle (the loop
  over `TableBody`, `:127`). RUN: the outlines `header only, no body rows <z>` and `no table at all <z>` make none.
- Tags, descriptions, the Examples block's name and step keywords are never passed to `Interpolate`. RUN: neither
  `first block` nor `second block` appears in a pickle's name.

### 1.4 The values it reads are the parser's, trimmed and unescaped

`Interpolate` reads `TableCell.Value` from the messages `GherkinDocument` that `AstMessagesConverter` builds from the
parser's AST, and the converter copies each cell's value as it is (35.0.0 `AstMessagesConverter.cs:181-187`;
`UseDefault` turns only null or empty into `""`, `CucumberMessagesDefaults.cs:7-13`):

```csharp
    private TableCell ConvertCell(Ast.TableCell c)
    {
        return new TableCell(
            ConvertLocation(c.Location),
            CucumberMessagesDefaults.UseDefault(c.Value, CucumberMessagesDefaults.DefaultCellValue)
        );
    }
```

The parser makes each cell's value (35.0.0 `GherkinLine.cs:11` and `:225-304`):

```csharp
    private static readonly char[] inlineWhitespaceChars = [' ', '\t', '\u00A0'];
```

```csharp
        void EnsureCellText(ref string cell, bool trim)
        {
            if (cell is not null)
            {
                if (trim)
                    cell = cell.TrimEnd(inlineWhitespaceChars);
                return;
            }

            while (startPos < pos && Array.IndexOf(inlineWhitespaceChars, lineText[startPos]) != -1)
                startPos++;

            int trimedPos = pos - 2;
            if (trim)
            {
                while (trimedPos >= startPos && Array.IndexOf(inlineWhitespaceChars, lineText[trimedPos]) != -1)
                    trimedPos--;
            }

            cell = lineText.Substring(startPos, trimedPos - startPos + 1);
        }

        public bool MoveNext()
        {
            string cell = null;

            while (pos < lineText.Length)
            {
                char c = lineText[pos];
                pos++;
                if (c == GherkinLanguageConstants.TABLE_CELL_SEPARATOR_CHAR)
                {
                    if (isFirstRow)
                    {
                        isFirstRow = false;
                        startPos = pos;
                    }
                    else
                    {
                        EnsureCellText(ref cell, true);

                        Current = new GherkinLineSpan(startPos + 1, cell);
                        startPos = pos;
                        return true;
                    }
                }
                else if (c == GherkinLanguageConstants.TABLE_CELL_ESCAPE_CHAR)
                {
                    EnsureCellText(ref cell, false);
                    if ((pos + 1) < lineText.Length)
                    {
                        c = lineText[pos];
                        pos++;
                        if (c == GherkinLanguageConstants.TABLE_CELL_NEWLINE_ESCAPE)
                        {
                            cell += "\n";
                        }
                        else
                        {
                            if (c != GherkinLanguageConstants.TABLE_CELL_SEPARATOR_CHAR && c != GherkinLanguageConstants.TABLE_CELL_ESCAPE_CHAR)
                            {
                                cell += GherkinLanguageConstants.TABLE_CELL_ESCAPE_CHAR;
                            }
                            cell += c;
                        }
                    }
                    else
                    {
                        cell += GherkinLanguageConstants.TABLE_CELL_ESCAPE_CHAR;
                    }
                }
                else
                {
                    if (cell is not null)
                        cell += c;
                }
            }

            return false;
        }
```

- **Trimming:** space, tab and U+00A0 only, at both ends (`inlineWhitespaceChars`); a line feed made by `\n` is kept at
  either end.
- **Escapes:** `\n` gives a line feed, `\|` gives `|`, `\\` gives `\`, and a backslash before any other character is kept
  with it (`\t` stays two characters). RUN, Appendix A.3: the cell `p\|q\\r\ns\tt\xu` reads `p|q\r`, a line feed,
  `s\tt\xu`.
- **INFERRED, not run:** the escape is honoured only when at least one character follows the escaped one (`:274`), so a
  backslash just before a line's closing pipe, `| a\|` with nothing after it, is kept as a backslash and the pipe closes
  the cell (`a\`), while `| a\| |` reads `a|`.

The title is trimmed with `string.Trim()` (all Unicode white space) by `GetRestTrimmed` (`GherkinLine.cs:96-99`), called
from `MatchTitleLine` (`TokenMatcher.cs:147-159`), and is not unescaped: a backslash in a title is literal (RUN: the
`escapes` outline).

```csharp
    public string GetRestTrimmed(int length)
    {
        return lineText.Substring(trimmedStartIndex + length).Trim();
    }
```

```csharp
    private bool MatchTitleLine(Token token, TokenType tokenType, string[] keywords)
    {
        foreach (var keyword in keywords)
        {
            if (token.Line.StartsWithTitleKeyword(keyword))
            {
                var title = token.Line.GetRestTrimmed(keyword.Length + GherkinLanguageConstants.TITLE_KEYWORD_SEPARATOR.Length);
                SetTokenMatched(token, tokenType, keyword: keyword, text: title);
                return true;
            }
        }
        return false;
    }
```

### 1.5 The rule, point by point

1. **Order.** The header cells in column order, left to right: `foreach (var variableCell in variableCells)`, each paired
   by index with `valueCells.ElementAt(col++)`. Each pass is `name = name.Replace("<" + header + ">", value)` on the string
   the previous pass returned. `string.Replace` replaces every non-overlapping occurrence, left to right, and does not
   look again at what it inserted. RUN: the rows of `order [<a>] then [<b>]`.
   **INFERRED, not run:** a column fills any `<header>` present when its turn comes, one formed across the edge of an
   earlier value included: title `<a>b>` with `a` = `<` and a later column `b` becomes `<b>` after the first pass, and
   column `b` then fills it. A reimplementation must therefore make the same sequential whole-string replaces. One regex
   pass over the authored title, or a lookup of each `<...>` in a header-to-value map, gives a different name for a row
   like point 3's, and for point 4's unless the map keeps the leftmost of a repeated header.
2. **No matching column.** Left as written. RUN: `unknown [<missing>] and known [1]`; and per block: the basket outline's
   second Examples block has no `display` column, so that row's step keeps `"<display>"`.
3. **A value that holds `<another header>`.** Filled again when that header's column is to the right of the value's
   column; left as written when it is to the left, or is the value's own column. RUN: `| <b> | B |` gives
   `order [B] then [B]`, `| A | <a> |` gives `order [A] then [<a>]`, `| x<a>y | y |` gives `order [x<a>y] then [y]`.
4. **Duplicate headers.** Gherkin's parser accepts `| d | d |` (RUN, no error) and the leftmost column wins, since its
   pass replaces every occurrence and the next finds none. RUN: `duplicate [1]` for the row `| 1 | 2 |`. Reqnroll refuses
   such a feature at generation (1.6).
5. **Empty value.** The placeholder is replaced with nothing. RUN: `empty value [] here`, step `[]`. An empty header cell
   is the header `""` and fills `<>`. RUN: `empty header [E] [H]`. **INFERRED, not run:** a title made only of
   placeholders whose values are all empty compiles to the empty name `""` (`"<e>".Replace("<e>", "")`).
6. **Header trimming.** Yes, by the parser (1.4), never by `Interpolate`, which wraps the header as it is: `< s >` does not
   match the header `s`, and `<the case>` matches the header `the case`, inner space kept. RUN:
   `spaced [v1] [< s >] [v2]`. The title is trimmed too. RUN: the title written as `   padded title <p>   ` gives
   `padded title P`.
7. **Case.** Ordinal and case-sensitive: .NET's `string.Replace(string, string)` is an ordinal replace, Java's
   `String.replace(CharSequence, CharSequence)` is literal, and JavaScript's escaped regex has no `i` flag. RUN:
   `case [lower] [<C>]`.
8. **Unescaped values.** Yes, headers and values alike (1.4). RUN: the value `p\|q\\r\ns\tt\xu` fills as `p|q\r`, a line
   feed, `s\tt\xu`, so a pickle's name can hold a line feed and a pipe; the header written `a\|b` is `a|b`, so `<a|b>` is
   filled and `<a\|b>` stays.
9. **Two more.** A row with fewer cells than its header is a parse error, `(6:7): inconsistent cell count within the
   table` (RUN), so `ElementAt(col++)` never runs past a row. Two rows can fill to one name, and both pickles keep it.
   RUN: `same filled name one` twice, ast node ids `[76, 73]` and `[76, 74]`.

### 1.6 What Reqnroll refuses before the compiler runs

**READ**, Reqnroll.Parser at 33eeea2. `ReqnrollGherkinParser.Parse` runs four semantic validators after Gherkin's parse
and throws on any error they find (`ReqnrollGherkinParser.cs:14-20`, `:107-115`, `:132-146`); they see every scenario of
the feature, those inside a `Rule:` included (`ReqnrollFeature.cs:16-23`). Reqnroll makes a `ScenarioOutline` only of a
scenario that has Examples (`ReqnrollGherkinParser.cs:69-79`).

```csharp
        private static readonly List<ISemanticValidator> semanticValidators = new List<ISemanticValidator>(4)
        {
            new DuplicateScenariosValidator(),
            new DuplicateExamplesValidator(),
            new MissingExamplesValidator(),
            new DuplicateExamplesColumnHeadersValidator()
        };
```

```csharp
            protected override Scenario CreateScenario(IEnumerable<Tag> tags, Location location, string keyword, string name, string description, IEnumerable<Step> steps, IEnumerable<Examples> examples, AstNode node)
            {
                ResetBlock();

                if (examples.Any())
                {
                    return new ScenarioOutline(tags, location, keyword, name, description, steps, examples);
                }

                return base.CreateScenario(tags, location, keyword, name, description, steps, examples, node);
            }
```

- `DuplicateExamplesColumnHeadersValidator` (`:16-29`) groups a block's header cells by value (the default string
  comparer, so ordinal and case-sensitive) and makes each repeat an error: point 4 cannot reach a Reqnroll run, though
  headers that differ only in case can.

```csharp
                    foreach (var example in scenarioOutline.Examples)
                    {
                        if (example.TableHeader != null)
                        {
                            var duplicateExamples = example.TableHeader.Cells
                                                           .GroupBy(g => g.Value)
                                                           .Where(g => g.Count() > 1);

                            foreach (var duplicateExample in duplicateExamples)
                            {
                                var message = $"Scenario Outline '{scenarioOutline.Name}' already contains an example column with header '{duplicateExample.Key}'";
                                errors.Add(new SemanticParserException(message, scenarioOutline.Location));
                            }
                        }
```

- `MissingExamplesValidator` (`:28-31`): an outline any of whose Examples blocks has no body rows, or no table, is an
  error. So every row of a Reqnroll outline has a header, and the probe's header-only and table-less blocks cannot occur.
  A `Scenario Outline:` with no `Examples:` at all is not an outline to Reqnroll and is compiled as a plain scenario, as by
  Gherkin.

```csharp
        private static bool DoesntHavePopulatedExamples(ScenarioOutline scenarioOutline)
        {
            return !scenarioOutline.Examples.Any() || scenarioOutline.Examples.Any(x => x.TableBody == null || !x.TableBody.Any());
        }
```

- `DuplicateScenariosValidator` (`:11-16`): two scenarios of one feature with one name, as written, are an error. Rows of
  one outline that fill to one name are not checked, so point 9's duplicate names can reach a Reqnroll run.
- `DuplicateExamplesValidator` (`:17-20`): two named Examples blocks of one outline with one name, neither tagged
  `@ignore`, are an error.

### 1.7 Java (Cucumber-JVM) and JavaScript use the same rule

**READ.** Java at 9bdaf5b, `PickleCompiler.java:262-276`: background steps are not filled, and `interpolate` is the .NET
loop with an index:

```java
    private PickleStep pickleBackgroundStep(Step step, StepKeywordType keywordType) {
        // Background steps are not interpolated
        return pickleStep(step, emptyList(), null, keywordType);
    }

    private String interpolate(String name, List<TableCell> variableCells, List<TableCell> valueCells) {
        for (int i = 0, variableCellsSize = variableCells.size(); i < variableCellsSize; i++) {
            TableCell variableCell = variableCells.get(i);
            TableCell valueCell = valueCells.get(i);
            String header = variableCell.getValue();
            String value = valueCell.getValue();
            name = name.replace("<" + header + ">", value);
        }
        return name;
    }
```

It is called for the name (`:166`), the step text (`:210`), the doc string's media type and content (`:201-204`) and the
data table's cells (`:191`).

Cucumber-JVM 7.18.1 uses Gherkin 28.0.0 (`cucumber-bom/pom.xml:18` at v7.18.1:
`<gherkin.version>28.0.0</gherkin.version>`), whose `interpolate` (`PickleCompiler.java:253-262` at v28.0.0) has the
for-each form .NET has; it is called at `:173` (name), `:192` (cells), `:201-202` (doc string) and `:218` (step text):

```java
    private String interpolate(String name, List<TableCell> variableCells, List<TableCell> valueCells) {
        int col = 0;
        for (TableCell variableCell : variableCells) {
            TableCell valueCell = valueCells.get(col++);
            String header = variableCell.getValue();
            String value = valueCell.getValue();
            name = name.replace("<" + header + ">", value);
        }
        return name;
    }
```

Cucumber-JVM names a test case from its pickle (v7.18.1, `cucumber-core/src/main/java/io/cucumber/core/runner/TestCase.java:116-119`
and `cucumber-gherkin-messages/src/main/java/io/cucumber/core/gherkin/messages/GherkinMessagesPickle.java:76-79`):

```java
    @Override
    public String getName() {
        return pickle.getName();
    }
```

```java
    @Override
    public String getName() {
        return pickle.getName();
    }
```

Java's cell parser has the same three escapes (`TableRowLine.java:24-34` at 9bdaf5b) but trims a wider set of white space:
Unicode `Zs` and the WS, B and S directionality classes, line feeds excepted (`StringUtils.java:17-19`, `:32-67`,
`:93-107`). **INFERRED, not run:** a header padded with, say, U+2003 is `s` to Java's parser and keeps the U+2003 to .NET's,
a difference in the parsers rather than in the rule, which only a producer whose files use such characters would see.

```java
        while (iterator.hasNext()) {
            int c = iterator.next();
            if (escape) {
                switch (c) {
                    case 'n' -> cellBuilder.append('\n');
                    case '\\' -> cellBuilder.append('\\');
                    case '|' -> cellBuilder.append('|');
                    // Invalid escape. We'll just ignore it.
                    default -> cellBuilder.append("\\").appendCodePoint(c);
                }
                escape = false;
```

JavaScript at 9bdaf5b (`compile.ts:282-298`; called for the name `:214`, the doc string `:231-233`, table cells `:250` and
step text `:315`): the header is regex-escaped, the `g` flag replaces every occurrence, and a `$` in the value is doubled
so the value goes in literally. The same rule.

```ts
function interpolate(
  name: string,
  variableCells: readonly TableCell[],
  valueCells: readonly TableCell[]
) {
  variableCells.forEach((variableCell, n) => {
    const valueCell = valueCells[n]
    const valuePattern = `<${variableCell.value}>`
    const escapedPattern = valuePattern.replace(/[-/\\^$*+?.()|[\]{}]/g, '\\$&')
    const regexp = new RegExp(escapedPattern, 'g')
    // JS Specific - dollar sign needs to be escaped with another dollar sign
    // https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/String/replace#Specifying_a_string_as_a_parameter
    const replacement = valueCell.value.replace(/\$/g, '$$$$')
    name = name.replace(regexp, replacement)
  })
  return name
}
```

### 1.8 The probe (RUN)

Gherkin's own parser (`Parser.Parse`), converter (`AstMessagesConverter`) and compiler (`PickleCompiler.Compile`), from
the NuGet cache, in a `dotnet run --file` app over a feature written to cover the points above. It prints each outline's
AST values (the parser's), then each pickle's name, steps, doc string and table, then the parse error for a short row. It
was run against Gherkin 35.0.0 and again against 42.0.1 (the version whose compiler is 9bdaf5b's); the two outputs are
identical but for their first line. The feature, the app and the output are in Appendix A. Both runs finished before the
coordinator's build hold, with .NET SDK 10.0.300:

```bash
cd C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/4446b339-7f87-4b87-990f-495a136f5c68/scratchpad/plan101/gherkin/probe/v35
dotnet run --file probe.cs -- ../outlines.feature > output.txt 2>&1
cd ../v42
dotnet run --file probe.cs -- ../outlines.feature > output.txt 2>&1
```

## 2. Kronikol's Cucumber lane

**READ**, `C:/Code/Kronikol-101` at `eabec2bd`; files under `src/Kronikol/Ingestion/Cucumber/`, and line numbers in
`CucumberFeatureSynthesizer.cs` unless another file is named.

### 2.1 How an outline row is built

- **The row.** `FindExampleRow` takes the pickle's second ast node id, the Examples row's (`:1078-1083`), and
  `GherkinIndex.FindExampleRow` looks it up (`GherkinIndex.cs:141-142`).
- **DisplayName** (`:489`, set at `:724`): the pickle's name, which Gherkin filled; the title as written
  (`node.Node.Name`, from the `gherkinDocument` envelope) only when the pickle's name is null or empty; the scenario id when
  there is neither.

```csharp
        var featureName = node?.Feature?.Name is { Length: > 0 } fn ? fn : options.DefaultFeatureName;
        var displayName = pickle.Name is { Length: > 0 } pn ? pn : node?.Node.Name ?? scenarioId;
```

- **OutlineId** (`:743`): the title as written, unfilled, on every row of an outline. **ExampleValues** and
  **ExampleRawValues** (`:719`, `:744-745`), and the Examples block's name, description and index (`:746-748`):

```csharp
            // The pickle carries the uri too, and is the fallback when the gherkinDocument for this
            // scenario never arrived (a producer may emit pickles without documents).
            SourceFile = NormalisePath(node?.Uri ?? pickle.Uri),
            SourceLine = node?.Node.Location?.Line,
            OutlineId = exampleRow is null ? null : node?.Node.Name,
            ExampleValues = exampleValues,
            ExampleRawValues = exampleRaw,
            ExamplesBlockName = exampleRow?.BlockName,
            ExamplesBlockDescription = exampleRow is null ? null : NullIfBlank(Dedent(exampleRow.BlockDescription)),
            ExamplesBlockIndex = exampleRow?.BlockIndex,
```

```csharp
    private static (Dictionary<string, string>? Values, Dictionary<string, object?>? Raw) BuildExampleValues(ExampleRow? row)
    {
        if (row is null || row.Values.Count == 0)
            return (null, null);
        var values = new Dictionary<string, string>(row.Values, StringComparer.Ordinal);
        var raw = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in row.Values)
            raw[key] = value;
        return (values, raw);
    }

    private static ExampleRow? FindExampleRow(CucumberPickle pickle, GherkinIndex gherkin)
    {
        if (pickle.AstNodeIds is not { Length: > 1 })
            return null;
        return gherkin.FindExampleRow(pickle.AstNodeIds[1]);
    }
```

  The values are header to cell, in column order, from the `gherkinDocument`'s Examples table (`GherkinIndex.cs:100-128`):
  the same parser values Gherkin's `Interpolate` reads, unescaped and trimmed by whichever Gherkin the producer ran. A
  block with no header is skipped, and a short row is padded with `""` (which a valid parse never needs, 1.5 point 9).

```csharp
    private void AddExamples(CucumberScenarioNode scenario)
    {
        var blocks = scenario.Examples ?? [];
        for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
            var examples = blocks[blockIndex];
            var headers = (examples.TableHeader?.Cells ?? []).Select(c => c.Value ?? "").ToArray();
            if (headers.Length == 0)
                continue;
            var tags = (examples.Tags ?? [])
                .Select(t => CucumberFeatureSynthesizer.Strip(t.Name))
                .Where(t => t is not null)
                .Select(t => t!)
                .ToArray();
            var blockName = CucumberFeatureSynthesizer.NullIfBlank(examples.Name);
            var blockDescription = CucumberFeatureSynthesizer.NullIfBlank(examples.Description);

            foreach (var row in examples.TableBody ?? [])
            {
                if (row.Id is not { Length: > 0 } rowId)
                    continue;
                var cells = (row.Cells ?? []).Select(c => c.Value ?? "").ToArray();
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var i = 0; i < headers.Length; i++)
                    values[headers[i]] = i < cells.Length ? cells[i] : "";
                _exampleRows[rowId] = new ExampleRow(values, tags, blockName, blockDescription, blockIndex);
            }
        }
    }
```

- **ExampleDisplayName:** never set by this lane; nothing under `src/Kronikol/Ingestion/Cucumber/` names it. In all of
  `src/`, only `Reports/Merge/MergeableReportReader.cs:267` sets it (reading a mergeable file back);
  `Reports/ReportGenerator.cs:3822` (`s.ExampleDisplayName ?? s.DisplayName`), `:5378` and `Reports/ParameterGrouper.cs:119-120`
  read it.
- **Scenario.Id** (`:304`; `ResolveScenarioId`, `:834-880`): the attempt's `kronikol-test-id` attachment, the first
  scenario to use a value keeping it, otherwise the minted `{pickle.Id}#{attempt}` (`:877`).
- **Stable id:** not stored. Each consumer computes `ScenarioStableId.Compute(suite, feature.DisplayName,
  scenario.DisplayName, scenario.OutlineId, scenario.ExampleValues)`: `Reports/ReportGenerator.cs:2495` and `:5355`,
  `History/HistoryRunBuilder.cs:58`, `Reports/FailuresDigestGenerator.cs:267`, `Reports/Merge/MergeableReportMerger.cs:371-372`
  among others. The feature here is the `Feature:` title (`:307`). So a Cucumber-lane row hashes
  `{suite}::{feature}::{title as written}::{filled name}::{header=value pairs sorted by header}`
  (`src/Kronikol/Reports/ScenarioStableId.cs:33-53`):

```csharp
    public static string Compute(string? suite, string featureName, string scenarioDisplayName, string? outlineId = null,
        IReadOnlyDictionary<string, string>? exampleValues = null)
    {
        var input = $"{featureName}::{scenarioDisplayName}";
        if (outlineId is not null)
            input = $"{featureName}::{outlineId}::{scenarioDisplayName}";

        if (!string.IsNullOrEmpty(suite))
            input = $"{suite}::{input}";

        if (exampleValues is { Count: > 0 })
        {
            var ordered = string.Join("|", exampleValues
                .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                .Select(kvp => $"{kvp.Key}={kvp.Value}"));
            input = $"{input}::{ordered}";
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
```

### 2.2 Does the lane ever give a row the raw title?

- **OutlineId:** always, by design (`:743`).
- **DisplayName:** only through the fallback at `:489`, when the pickle's name is null or empty. **INFERRED, not run:**
  Gherkin gives a row an empty name when its outline's title is empty, or is made only of placeholders whose values are
  all empty (`Scenario Outline: <e>` with an empty `e` cell, 1.5 point 5). That row is then named `<e>`, the raw title,
  where the messages say `""`. A producer that leaves `name` out gets the same.
- **Nothing pins the filled name today:** no test or fixture under `tests/` has a placeholder in an outline's title or in a
  pickle's name (grep over `*.cs`, `*.ndjson`, `*.json` and `*.feature`); the playwright-bdd fixture's only outline is
  `An outline over pages` (`tests/Kronikol.Tests/TestData/Cucumber/playwright-bdd-9.2-messages.ndjson`,
  `CucumberFixtures.cs:66`). The only feature files in the checkout with a placeholder title are this plan's own, under
  `OUTLINE_ROW_NAMES_PLAN.harness/probe/`.
- **A doc comment that says the opposite.** `CucumberMessages.cs:278-279` reads:

```csharp
    /// <summary>Scenario name. For an outline row this is the outline name, <em>unexpanded</em>.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }
```

  A pickle's name is expanded: Gherkin fills it (1.3; RUN, Appendix A.3), and `:489` relies on that. The comment should
  say that an outline row's pickle is named by its outline's title with the row's values filled in.

### 2.3 Two rows that fill to one name

Nothing in the lane renames, suffixes or merges them. The merger keys scenarios by `Scenario.Id`
(`CucumberFeatureMerger.cs:58-64`, `:72-73`), the test names are keyed by scenario id (`:326`), and the feature
accumulator by feature name alone (`:313-323`). The two rows keep one DisplayName and two `Id`s: two pickles never share
an id, and a `kronikol-test-id` used twice falls back to a minted id with a warning (`:860-871`). Their stable ids differ
as long as their ExampleValues do, so for any two different rows. **INFERRED, not run:** a row repeated cell for cell gives
two scenarios with one stable id (`ScenarioStableId.cs:36-49`), which a merge counts as one scenario run twice unless the
attempt, result, duration or message differs (`MergeableReportMerger.cs:365-385`).

One difference from Gherkin's own values that the lane already has (**READ**, effect **INFERRED**): with duplicate headers,
Gherkin's name takes the leftmost column (1.5 point 4), while `GherkinIndex.cs:123-124` assigns `values[headers[i]]` in a
loop, so ExampleValues keeps the rightmost. Reqnroll refuses duplicate headers (1.6); that the Java and JavaScript parsers
accept them, as the .NET one does, was not checked.

### 2.4 For the plan's stable-id question (INFERRED)

Today the Cucumber lane hashes `{feature}::{title as written}::{filled name}::{values}`, and, by the issue's account of the
Reqnroll lane (DisplayName and OutlineId both the title as written), the Reqnroll lane hashes
`{feature}::{title as written}::{title as written}::{values}`. For a title without placeholders the two inputs are the same
whenever the suite, the feature name and the example values agree. For a title with placeholders they differ today.
Hashing the filled name in the Reqnroll lane would give the two lanes one input on those same terms, and each such row a
new id once; hashing the raw title would keep today's Reqnroll ids, and the lanes would go on disagreeing as they do now. Changing
`ScenarioStableId.Compute` itself for outline rows would move the Cucumber lane's ids too, and Kronikol4J's (3.1). That the
Reqnroll lane's ExampleValues use the same keys and strings as this lane's is for the Reqnroll research to confirm.

## 3. Kronikol4J

**READ**, `C:/Code/Kronikol4J`. The files below are the same at local `main` (`92d6b28`) and `origin/main` (`b6a5dac`)
(`git diff --quiet 92d6b28 origin/main -- <file>` for each).

### 3.1 The Cucumber-JVM lane

`kronikol4j-cucumber` is a Cucumber `EventListener`, compiled against `io.cucumber:cucumber-plugin:7.18.1` (`compileOnly`;
`kronikol4j-cucumber/build.gradle.kts:9-13`), which the user's own Cucumber runs.
`kronikol4j-cucumber/src/main/java/io/kronikol/cucumber/KronikolCucumberPlugin.java:40-44`, `:53-59` and `:77-86`:

```java
    private void onTestCaseStarted(TestCaseStarted event) {
        TestCase testCase = event.getTestCase();
        SCOPE.set(TestIdentityScope.begin(testCase.getName(), testCase.getId().toString()));
        TestPhaseContext.reset();
    }
```

```java
    private void onTestCaseFinished(TestCaseFinished event) {
        TestCase testCase = event.getTestCase();
        Result result = event.getResult();
        RunResults.record(featureName(testCase.getUri()),
            new Scenario(testCase.getName(), testCase.getId().toString(),
                mapStatus(result.getStatus()), 0,
                result.getError() == null ? null : String.valueOf(result.getError())));
```

```java
    static String featureName(URI uri) {
        if (uri == null) {
            return "Cucumber";
        }
        String path = uri.getSchemeSpecificPart();
        int slash = path.lastIndexOf('/');
        String file = slash >= 0 ? path.substring(slash + 1) : path;
        int dot = file.lastIndexOf('.');
        return dot > 0 ? file.substring(0, dot) : file;
    }
```

- **Name:** `testCase.getName()`, which in Cucumber-JVM is the pickle's name (1.7), for the identity scope (`:42`) and the
  recorded scenario (`:57`): a row is named as Gherkin fills it.
- **No outline id and no example values:** the five-argument `Scenario` constructor passes null for both
  (`kronikol4j-report/src/main/java/io/kronikol/report/model/Scenario.java:58-62`).
- **Stable id:** `Scenario.stableId(featureName)` calls `ScenarioStableId.compute(featureName, name, outlineId,
  exampleValues)` (`Scenario.java:53-56`), which with both null hashes `feature::name`, the filled name
  (`kronikol4j-report/src/main/java/io/kronikol/report/model/ScenarioStableId.java:32-61`). The feature the plugin records
  is the feature file's name without its extension (`KronikolCucumberPlugin.java:56`, `:77-86`), not the `Feature:` title
  .NET uses; that the report passes that name to `stableId` is **INFERRED** (the 5.2.7 ledger line: "`RunResults.toFeatures`
  builds a feature from its name and scenarios").

```java
    /** The deterministic, cross-run stable id derived from the owning feature + this scenario. */
    public String stableId(String featureName) {
        return ScenarioStableId.compute(featureName, name, outlineId, exampleValues);
    }

    /** Back-compatible core scenario (no BDD / parameterized metadata). */
    public Scenario(String name, String testId, ExecutionStatus status, long durationMs, String error) {
        this(name, testId, status, durationMs, error, false, null, List.of(), List.of(),
            null, null, null, null, null, List.of(), List.of(), List.of());
    }
```

```java
    public static String compute(String featureName, String scenarioDisplayName, String outlineId,
                                 Map<String, String> exampleValues) {
        String input = outlineId != null
            ? featureName + "::" + outlineId + "::" + scenarioDisplayName
            : featureName + "::" + scenarioDisplayName;

        if (exampleValues != null && !exampleValues.isEmpty()) {
            List<String> keys = new ArrayList<>(exampleValues.keySet());
            keys.sort(null);
            StringBuilder ordered = new StringBuilder();
            for (String key : keys) {
                if (ordered.length() > 0) {
                    ordered.append('|');
                }
                ordered.append(key).append('=').append(exampleValues.get(key));
            }
            input = input + "::" + ordered;
        }
```

- **Outside #101:** the port's `compute` takes no suite (`ScenarioStableId.java:22-24`, `:32-33`), where .NET's has
  prefixed one since 3.1.0 (`ScenarioStableId.cs:18-25`, `:40-41`). No ledger line mentions a suite in the stable id (the
  only `suite` near a hash in the ledger is the history roster's `sha256(suite + ids)[..16]`).

### 3.2 No lane that gets a raw Gherkin title

The port has no Reqnroll-like adapter, and Cucumber is its only Gherkin lane. Its other runners name a test with the
runner's own name for the invocation: Spock `iteration.getDisplayName()`
(`kronikol4j-spock/src/main/java/io/kronikol/spock/KronikolSpockExtension.java:50`, `:64`), JUnit 5
`context.getDisplayName()` (`kronikol4j-junit5/src/main/java/io/kronikol/junit5/KronikolExtension.java:33`, `:70`), TestNG
`result.getMethod().getMethodName()` (`kronikol4j-testng/src/main/java/io/kronikol/testng/KronikolTestNgListener.java:27`,
`:65`, `:68-70`). No main source outside `kronikol4j-report` sets an outline id or example values (grep for `outlineId(`,
`exampleValues(` and `exampleDisplayName(`). The nearest thing to #101 is TestNG, where every invocation of a
data-provider test is named by its method alone (**INFERRED** from `:68-70`; its id differs per invocation by
`System.identityHashCode`, `:72-74`): not a Gherkin title, and not in #101's scope.

### 3.3 The divergence ledger

`docs/REMAINING_PARITY.md`, section `## .NET-side features shipped after this audit (divergence ledger)` (line 1877): one
bullet per .NET release, appended at the end of the section, before the `---` that precedes
`## Explicitly OUT OF SCOPE (locked boundaries — do not implement)`.

The local `main` checkout (`92d6b28`) is behind: its ledger ends at .NET 4.16.3 (line 2436). `origin/main` (`b6a5dac`,
"Ledger: .NET 5.4.0, ...") carries the lines through 5.4.0, the section's last entries at lines 2498-2500; the plan's
line goes on top of `origin/main`. Read with `git show origin/main:docs/REMAINING_PARITY.md`, no checkout changed. The
last three entries, verbatim:

```markdown
- **Fixes from a review of 5.0.0 (.NET 5.2.7, 2026-10-22).** Not mirrored: the page's half is in code the port's page (3.0.43's) does not have in this form, and the rest is in .NET adapters, readers and the tool, which the port does not ship. What the page changes: `plantuml-browser-render-script.js`'s role pass gives `k-participant-fill` only to a participant whose fill is not a colour its declaration gives (`SequenceDiagramParticipantColors`), an engine anchor takes Space, and the diagram failure lines are written in `var(--kron-fail, #c00)`; `report-keyboard-navigation-function.js` is wrapped in a closure, the arrow keys focus the picked scenario's summary, and `/` is read before the tab list's guard; `report-filter-chips-script.js` ANDs the Happy Path chip's has with the row and finds a link's tag chip by ASCII case fold; `report-search-function.js` reads a qualifier by where it stands (quoted, inside a negated group, or at the top), takes `not` as a negation and a dangling legacy `and`/`or` with it, compares `dep:` names by Unicode letters and digits, refuses `duration:""`, evaluates both sides of each tag expression operator and splits brackets written against a tag (its VerdictFences pins move; fenced lines unchanged); `collapsible-notes-script.js` keeps the reader's place by the note's source index, previews braces only for one JSON object, and reads no note body or style block as a declaration in the setup filter (a one-line style block closes); `report-features-tools-script.js` and `report-url-hash-function.js` give a Summary row for a hidden feature the filtered-out line; `report-scenario-settings-script.js` opens a closed Diagrams section from its title's gear; the search help's qualifier sentence changes. C#: the NUnit, MSTest, xUnit v2, xUnit v3, TUnit and Reqnroll adapters and the Cucumber importer and merger take a feature's tags as those all its parts share (`ScenarioTags.Shared`); the tool's `--tag` matches Happy Path and a 4.x Happy Path label folds into the flag; `--run` finds a kept run under either extension; a run deletes its data file under the other name; `QueryCommand.Newer` and the skill's `query.py` prefer the file `Run.json` lists; merge refuses a `.krkl` it may not write. The port's adapters give a feature no tags (`RunResults.toFeatures` builds a feature from its name and scenarios), so the feature-tags fault cannot arise there. No data file schema changes; byte parity breaks on the page's scripts and on the search help.
- **A stack's start-up kept as a scenario of its own, other runs' traffic left to their reports, and the run window's entry with its figures first (.NET 5.3.0, 2026-10-22, `RUN_WINDOW_PLAN.md` R2, #187).** Nothing to port: `IngestRequest.SetupWindow`, `IngestRequest.OtherRunWindows`, `IngestResult.SetupRecords` and `OtherRunRecords`, and `kronikol ingest`'s `--setup-window`, `--setup-name` and `--other-run` are the .NET ingest's, and the port has no ingest layer, so it writes no `DroppedOutsideRunWindow` entry and no setup scenario. A port report merged with a .NET ingest's keeps the setup scenario as it keeps any scenario that is not a test (5.2.6's `notATest`).
- **A long payload scrolls in its box (.NET 5.4.0, 2026-10-22, `plans/LONG_NOTE_SCROLL_PLAN.md`).** Not mirrored: the port's page is 3.0.43's, and its expanded notes are still cut at 15,000 characters across diagrams. .NET's BrowserJs page now draws an expanded payload longer than `PayloadScrollLines` (a new option, 40 by default, 0 for the drawing in full) that many lines tall, with a `...` row and a clear row as wide as its widest line, and lays a scroll box over its body (`<foreignObject class="note-scroll-frame">`) that holds the whole body as text. The report HTML changes in the embedded scripts and stylesheet and in one line the generator writes, `window._payloadScrollLines = 40;`, and the Expanded control's title gains "; a long one scrolls in its box". One fix in the same release applies to the port: "Copy box text" on a FocusFields payload copied Kronikol's markup as text (`<b>`, `<color:lightgray>`); .NET's `noteLinesToText` now takes off the tags FocusFields writes (`window._stripNoteEmphasis`). The port's copy takes off only a leading `<color:gray>` (`context-menu-script.js` :375 and :394 at 33ad170) and its `JsonFocusFormatter` writes the same tags, so it copies them the same way: worth porting. So is the second copy fix: a form body's fields copied with the grey divider's tag, `a=1<font color="lightgray">&`, which the port's generator writes the same way; .NET's copies now drop the tag and keep the `&`.
```

The shape: `- **<what .NET changed, as one sentence> (.NET <version>, <yyyy-mm-dd>, <plan file and release>, #<issue>).**`
then a verdict that opens with "Not mirrored", "Nothing to port", "Worth porting" or "Parity gained" (or "Partly ..."),
says why with the port's own file:line (sometimes "at <commit>"), names any byte-parity break, and stops. Every entry
from .NET 3.34.2 on is one physical line (the earlier ones wrap at about 110 columns), and no 5.x entry has an em-dash
(two 4.x entries, 4.17.0's and 4.31.0's, do). Counted over the section's 143 entries at `origin/main`.

A draft for this fix, its version, date and release to be filled in, and its middle sentence chosen by what R1 does to the
stable id:

```markdown
- **A Reqnroll outline row is named as Gherkin names its pickle, the title's `<header>` placeholders filled from the row's Examples values (.NET 5.x.y, 2026-10-xx, `plans/OUTLINE_ROW_NAMES_PLAN.md` R1, #101).** Nothing to port: the port has no Reqnroll adapter, and its one Gherkin lane, `KronikolCucumberPlugin`, already names a row `testCase.getName()`, the pickle's name, which Cucumber-JVM's Gherkin fills by the same rule (`KronikolCucumberPlugin.java:42`, `:57`); it records no outline id and no example values (`Scenario.java:58-62`), so its rows' stable ids (`ScenarioStableId.java:32-61`) do not move. [If R1 changes what `ScenarioStableId.Compute` hashes: "Worth porting: `ScenarioStableId.compute` mirrors .NET's (`ScenarioStableId.java:11-16`), and <the change>; `ScenarioStableIdTest` pins it." If only the Reqnroll adapter's inputs change, drop this sentence.] Report output changes only in a .NET Reqnroll report: an outline row's name wherever it is listed on its own, and its stable id if R1 hashes the filled name.
```

## 4. Outside the brief, for the plan

- **The Reqnroll adapter already finds the row's pickle.** `src/Kronikol.ReqNRoll.Core/ExamplesBlockResolver.cs:26-31`,
  `:92-100`, `:116-135` and `:172-178` (READ): it reads Reqnroll's embedded feature messages (Gherkin document and
  pickles) by reflection and finds the running row's pickle by `ScenarioInfo.PickleId`, else by `PickleIdIndex`, checked
  against the row's argument values. That pickle's `Name` is Gherkin 35.0.0's filled name (1.1), so wherever the messages
  are present the fix could read it, and fill the title itself only where they are not (`HasMessages` false, a reflection
  miss). **INFERRED**; when Reqnroll embeds no messages was not checked.
- **Two lookups there must keep the raw title** (INFERRED): `ResolveSource` keys the declaration line by
  `scenarioInfo.Title` against the Gherkin document's scenario names (`:74`, `:183-184`), and the value-match fallback
  compares `e.ScenarioName` with `scenarioInfo.Title` (`:106`). Both are titles as written on both sides; a fix that fed
  them the filled name would miss every row whose title has a placeholder.

## Pending build steps

None. Both probe runs (Gherkin 35.0.0 and 42.0.1) had finished when the coordinator held builds, and none was started
after.

## Scratch folder

`C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/4446b339-7f87-4b87-990f-495a136f5c68/scratchpad/plan101/gherkin/`:

- `probe/outlines.feature`; `probe/v35/probe.cs` and `output.txt`; `probe/v42/probe.cs` and `output.txt`.
- `src-1adafde/` (Gherkin .NET 35.0.0: `PickleCompiler.cs`, `GherkinLine.cs`, `TokenMatcher.cs`, `AstMessagesConverter.cs`,
  `CucumberMessagesDefaults.cs`, `Parser.Extensions.cs`), `src-9bdaf5b/` (`PickleCompiler.cs`, `GherkinLine.cs`,
  `TokenMatcher.cs`), `src-33693f4/` (42.0.1's `PickleCompiler.cs`).
- `java-9bdaf5b/` (`PickleCompiler.java`, `TableRowLine.java`, `StringUtils.java`), `java-v28.0.0/` (`PickleCompiler.java`,
  `GherkinLine.java`), `js-9bdaf5b/compile.ts`.
- `reqnroll-33eeea2/` (`CucumberMessagesConverter.cs`, `ReqnrollGherkinParser.cs`, `ReqnrollFeature.cs` and the four
  validators), `cucumber-jvm-7.18.1/` (`TestCase.java`, `GherkinMessagesPickle.java`, the poms).
- `k4j-ledger-origin-main.md` (the ledger at Kronikol4J `origin/main`), and `doc/` (this file's template and the script that
  assembled it, splicing every quote and the probe's output from the files above).

## Appendix A. The probe

### A.1 `outlines.feature`

```gherkin
Feature: Outline row names

  Scenario Outline: A basket is weighed on the UK scale (<case>)
    Given a basket of <weight> grams
    Then the scale shows "<display>"

    Examples: first block
      | case  | weight | display |
      | empty | 0      | 0 g     |
      | full  | 1200   | 1.2 kg  |

    Examples: second block
      | case  | weight |
      | heavy | 5000   |

  Scenario Outline: order [<a>] then [<b>]
    Given step [<a>] [<b>]

    Examples:
      | a     | b   |
      | <b>   | B   |
      | A     | <a> |
      | x<a>y | y   |

  Scenario Outline: unknown [<missing>] and known [<k>]
    Given [<missing>] [<k>]

    Examples:
      | k |
      | 1 |

  Scenario Outline: duplicate [<d>]
    Given [<d>]

    Examples:
      | d | d |
      | 1 | 2 |

  Scenario Outline: empty value [<e>] here
    Given [<e>]

    Examples:
      | e |
      |   |

  Scenario Outline: spaced [<s>] [< s >] [<the case>]
    Given x

    Examples:
      |   s   | the case |
      |  v1   |  v2      |

  Scenario Outline: case [<c>] [<C>]
    Given x

    Examples:
      | c     |
      | lower |

  Scenario Outline: escapes [<v>] [<a|b>] [<a\|b>]
    Given x

    Examples:
      | v                | a\|b |
      | p\|q\\r\ns\tt\xu | w    |

  Scenario Outline: empty header [<>] [<h>]
    Given x

    Examples:
      |   | h |
      | E | H |

  Scenario Outline: doc string and table <m>
    Given a doc string
      """<m>
      body <m> and <n>
      """
    And a table
      | <m> | lit | <n> |

    Examples:
      | m    | n |
      | json | 7 |

  Scenario Outline:    padded title <p>
    Given x

    Examples:
      | p |
      | P |

  Scenario Outline: header only, no body rows <z>
    Given x

    Examples:
      | z |

  Scenario Outline: no table at all <z>
    Given x

    Examples:

  Scenario Outline: same filled name <same>
    Given x

    Examples:
      | same | other |
      | one  | 1     |
      | one  | 2     |
```

### A.2 `probe.cs`

The 35.0.0 copy; the 42.0.1 copy differs only in its first line, `#:package Gherkin@42.0.1`.

```csharp
#:package Gherkin@35.0.0
// Probe: parse a feature with Gherkin's own parser, compile pickles with Gherkin's own
// PickleCompiler, and print the AST cell values next to each pickle's name, steps,
// doc strings and data tables. Usage: dotnet run --file probe.cs -- <feature file>
using System.Reflection;
using System.Text;
using Gherkin;
using Gherkin.CucumberMessages;
using Gherkin.CucumberMessages.Pickles;

static string Esc(string? s)
{
    if (s is null) return "null";
    var sb = new StringBuilder("\"");
    foreach (var ch in s)
    {
        switch (ch)
        {
            case '\\': sb.Append("\\\\"); break;
            case '"': sb.Append("\\\""); break;
            case '\n': sb.Append("\\n"); break;
            case '\r': sb.Append("\\r"); break;
            case '\t': sb.Append("\\t"); break;
            default: sb.Append(ch); break;
        }
    }
    return sb.Append('"').ToString();
}

var asm = typeof(Parser).Assembly;
var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
Console.WriteLine($"Gherkin assembly {asm.GetName().Version} informational {info}");
Console.WriteLine("Strings are printed C#-escaped inside quotes: \\\\ is one backslash, \\n a newline, \\t a tab.");
Console.WriteLine();

var text = File.ReadAllText(args[0]);
var ast = new Parser().Parse(new StringReader(text));

Console.WriteLine("== AST (the parser's values)");
foreach (var child in ast.Feature.Children)
{
    if (child is not Gherkin.Ast.Scenario sc) continue;
    Console.WriteLine($"outline name {Esc(sc.Name)} keyword {Esc(sc.Keyword)}");
    foreach (var ex in sc.Examples)
    {
        var header = ex.TableHeader == null ? "null" : string.Join(" | ", ex.TableHeader.Cells.Select(c => Esc(c.Value)));
        Console.WriteLine($"  Examples {Esc(ex.Name)} header: {header}");
        foreach (var row in ex.TableBody ?? Enumerable.Empty<Gherkin.Ast.TableRow>())
            Console.WriteLine($"    row: {string.Join(" | ", row.Cells.Select(c => Esc(c.Value)))}");
    }
}
Console.WriteLine();

var ids = new IncrementingIdGenerator();
var doc = new AstMessagesConverter(ids).ConvertGherkinDocumentToEventArgs(ast, "outlines.feature");
var pickles = new PickleCompiler(ids).Compile(doc);

Console.WriteLine($"== Pickles ({pickles.Count})");
foreach (var p in pickles)
{
    Console.WriteLine($"pickle name {Esc(p.Name)} astNodeIds [{string.Join(", ", p.AstNodeIds)}]");
    foreach (var st in p.Steps)
    {
        Console.WriteLine($"  step {Esc(st.Text)}");
        if (st.Argument?.DocString is { } ds)
            Console.WriteLine($"    docString mediaType {Esc(ds.MediaType)} content {Esc(ds.Content)}");
        if (st.Argument?.DataTable is { } dt)
            foreach (var r in dt.Rows)
                Console.WriteLine($"    table row: {string.Join(" | ", r.Cells.Select(c => Esc(c.Value)))}");
    }
}
Console.WriteLine();

Console.WriteLine("== A row with fewer cells than its header");
try
{
    new Parser().Parse(new StringReader("Feature: f\n  Scenario Outline: bad <a>\n    Given x\n    Examples:\n      | a | b |\n      | 1 |\n"));
    Console.WriteLine("parsed without error");
}
catch (Exception e)
{
    Console.WriteLine($"{e.GetType().FullName}: {e.Message}");
}
```

### A.3 Output, Gherkin 35.0.0

```text
Gherkin assembly 35.0.0.0 informational 35.0.0+1adafde16942e8a0ea1e0b3223f9f6d12cb4bae1
Strings are printed C#-escaped inside quotes: \\ is one backslash, \n a newline, \t a tab.

== AST (the parser's values)
outline name "A basket is weighed on the UK scale (<case>)" keyword "Scenario Outline"
  Examples "first block" header: "case" | "weight" | "display"
    row: "empty" | "0" | "0 g"
    row: "full" | "1200" | "1.2 kg"
  Examples "second block" header: "case" | "weight"
    row: "heavy" | "5000"
outline name "order [<a>] then [<b>]" keyword "Scenario Outline"
  Examples "" header: "a" | "b"
    row: "<b>" | "B"
    row: "A" | "<a>"
    row: "x<a>y" | "y"
outline name "unknown [<missing>] and known [<k>]" keyword "Scenario Outline"
  Examples "" header: "k"
    row: "1"
outline name "duplicate [<d>]" keyword "Scenario Outline"
  Examples "" header: "d" | "d"
    row: "1" | "2"
outline name "empty value [<e>] here" keyword "Scenario Outline"
  Examples "" header: "e"
    row: ""
outline name "spaced [<s>] [< s >] [<the case>]" keyword "Scenario Outline"
  Examples "" header: "s" | "the case"
    row: "v1" | "v2"
outline name "case [<c>] [<C>]" keyword "Scenario Outline"
  Examples "" header: "c"
    row: "lower"
outline name "escapes [<v>] [<a|b>] [<a\\|b>]" keyword "Scenario Outline"
  Examples "" header: "v" | "a|b"
    row: "p|q\\r\ns\\tt\\xu" | "w"
outline name "empty header [<>] [<h>]" keyword "Scenario Outline"
  Examples "" header: "" | "h"
    row: "E" | "H"
outline name "doc string and table <m>" keyword "Scenario Outline"
  Examples "" header: "m" | "n"
    row: "json" | "7"
outline name "padded title <p>" keyword "Scenario Outline"
  Examples "" header: "p"
    row: "P"
outline name "header only, no body rows <z>" keyword "Scenario Outline"
  Examples "" header: "z"
outline name "no table at all <z>" keyword "Scenario Outline"
  Examples "" header: null
outline name "same filled name <same>" keyword "Scenario Outline"
  Examples "" header: "same" | "other"
    row: "one" | "1"
    row: "one" | "2"

== Pickles (17)
pickle name "A basket is weighed on the UK scale (empty)" astNodeIds [9, 3]
  step "a basket of 0 grams"
  step "the scale shows \"0 g\""
pickle name "A basket is weighed on the UK scale (full)" astNodeIds [9, 4]
  step "a basket of 1200 grams"
  step "the scale shows \"1.2 kg\""
pickle name "A basket is weighed on the UK scale (heavy)" astNodeIds [9, 7]
  step "a basket of 5000 grams"
  step "the scale shows \"<display>\""
pickle name "order [B] then [B]" astNodeIds [16, 12]
  step "step [B] [B]"
pickle name "order [A] then [<a>]" astNodeIds [16, 13]
  step "step [A] [<a>]"
pickle name "order [x<a>y] then [y]" astNodeIds [16, 14]
  step "step [x<a>y] [y]"
pickle name "unknown [<missing>] and known [1]" astNodeIds [21, 19]
  step "[<missing>] [1]"
pickle name "duplicate [1]" astNodeIds [26, 24]
  step "[1]"
pickle name "empty value [] here" astNodeIds [31, 29]
  step "[]"
pickle name "spaced [v1] [< s >] [v2]" astNodeIds [36, 34]
  step "x"
pickle name "case [lower] [<C>]" astNodeIds [41, 39]
  step "x"
pickle name "escapes [p|q\\r\ns\\tt\\xu] [w] [<a\\|b>]" astNodeIds [46, 44]
  step "x"
pickle name "empty header [E] [H]" astNodeIds [51, 49]
  step "x"
pickle name "doc string and table json" astNodeIds [58, 56]
  step "a doc string"
    docString mediaType "json" content "body json and 7"
  step "a table"
    table row: "json" | "lit" | "7"
pickle name "padded title P" astNodeIds [63, 61]
  step "x"
pickle name "same filled name one" astNodeIds [76, 73]
  step "x"
pickle name "same filled name one" astNodeIds [76, 74]
  step "x"

== A row with fewer cells than its header
Gherkin.CompositeParserException: Parser errors:
(6:7): inconsistent cell count within the table
```

### A.4 Output, Gherkin 42.0.1

The first line is `Gherkin assembly 42.0.1.0 informational 42.0.1+33693f4b5a76bb86dee61d0b287d6f2c700813e4`; every other
line is the same as in A.3 (`diff` of the two outputs after line 1 is empty).
