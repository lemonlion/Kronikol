import os, sys
root = sys.argv[1]
os.makedirs(root, exist_ok=True)
files = {
"R01_StoppedUnderWoven.feature": """Feature: R01 A hook ahead of ours stops a nested step inside a woven step

@r6-stop
Scenario: R01 one
  Given r6 composite runs a woven step that runs a stopped step with a table and doc
  Then an r step "not reached"

@r6-stop
Scenario: R01 two, the woven step throws after the stopped step
  Given r6 composite runs a woven step that runs a stopped step and throws
""",
"R02_AfterStepThrowsNested.feature": """Feature: R02 A suite's AfterStep hook throws on a nested step carrying a table, a doc string and an inline value

@r6-after-throws
Scenario: R02 one, not caught
  Given r6 composite runs a nested step the AfterStep hook fails

@r6-after-throws
Scenario: R02 two, caught and a second nested step runs
  Given r6 composite catches what an AfterStep hook threw and runs another

@r6-after-throws
Scenario: R02 three, control: the hook does not fail this nested step
  Given r6 composite runs a nested step the AfterStep hook passes
""",
"R03_Phase.feature": """Feature: R03 What the phase is after a nested step

@r6-stop
Scenario: R03 one, a When runs a Given a hook stops, then calls
  When r6 when composite runs a stopped given and calls

Scenario: R03 two, control, a When runs a Given that runs, then calls
  When r6 when composite runs a given and calls

@r6-stop
Scenario: R03 three, a Given runs a When a hook stops, then calls
  Given r6 given composite runs a stopped when and calls
""",
"R04_HooksRunSteps.feature": """Feature: R04 Hooks run steps while a nested step is open

@r6-before-runs
Scenario: R04 one, a BeforeStep hook after ours runs a step on a nested step
  Given r6 composite runs a nested step a hook adds to

@r6-after-runs
Scenario: R04 two, an AfterStep hook runs a step on a nested step
  Given r6 composite runs a nested step a hook adds to

@r6-first-before-runs
Scenario: R04 three, a BeforeStep hook ahead of ours runs a step on a nested step
  Given r6 composite runs a nested step a hook adds to

@r6-stop @r6-after-runs-stopped
Scenario: R04 four, an AfterStep hook on the scenario's step runs a step the first hook stops
  Given an r step "passes"
""",
"R05_RowsAndStops.feature": """Feature: R05 A Background whose nested step a hook stops for one row

Background:
  Given r6 composite runs a nested step named for its row

@r6-stop-row-b
Scenario Outline: R05 row <row>
  When an r step "<row>"

Examples:
  | row |
  | a   |
  | b   |
  | c   |
""",
"R06_TwoStoppedAndKeywords.feature": """Feature: R06 Keywords after a stopped nested step

@r6-stop
Scenario: R06 one, the steps after keep their keywords
  Given r6 composite runs a stopped and step
  And an r step "skipped"
  When an r step "skipped too"
""",
"R07_StopwatchKey.feature": """Feature: R07 The public stopwatch key read by a suite

@r6-read-stopwatch @r6-stop
Scenario: R07 one, a nested step a hook stops, the composite takes 300 ms
  Given r6 slow composite runs a stopped step

@r6-read-stopwatch
Scenario: R07 two, control, a nested step runs, the composite takes 300 ms
  Given r6 slow composite runs a step
""",
"R08_TopLevelThenHookRow.feature": """Feature: R08 A row with no frame

@r6-stop
Scenario: R08 one, the first step is stopped
  Given r6 step the hook stops at the top
  Then an r step "not reached"
""",
"R09_WovenAroundNestedAfterThrow.feature": """Feature: R09 A suite's AfterStep hook throws on a nested step inside a woven step

@r6-after-throws
Scenario: R09 one
  Given r6 composite runs a woven step around a nested step the AfterStep hook fails
""",
}
for name, text in files.items():
    with open(os.path.join(root, name), "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
print(len(files), "features")
more = {
"R10_AfterStepThrowsEverywhere.feature": """Feature: R10 A suite's AfterStep hook throws on every step

@r6-after-throws-all
Scenario: R10 one
  Given r6 composite runs a nested step the AfterStep hook passes
""",
"R11_WovenInAHook.feature": """Feature: R11 A woven step in a BeforeScenario hook, composites after it

@woven-hook
Scenario: R11 one
  Given an r composite runs an r step "x"
  Then an r step "y"
""",
"R13_CatchesThenWovenAndCalls.feature": """Feature: R13 A definition catches what the AfterStep hook threw on a nested step, then calls

@r6-after-throws
Scenario: R13 one
  Given r6 composite catches what an AfterStep hook threw then calls
  Then an r step "skipped"
""",
"R18_HookPends.feature": """Feature: R18 A hook ahead of ours ends a nested step in other ways

@r6-pending
Scenario: R18 one, Pending from the hook
  Given r6 composite runs an inner pending by hook

@r6-ignore
Scenario: R18 two, TestIgnore from the hook
  Given r6 composite runs an inner ignored by hook
  Then an r step "after it"
""",
}
for name, text in more.items():
    with open(os.path.join(root, name), "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
print(len(more), "more")
more = {
"R19_WovenInAHookPlain.feature": """Feature: R19 A woven step in a BeforeScenario hook, plain steps after it

@woven-hook
Scenario: R19 one
  Given an r step "x"
  When an r step "y"
  Then an r step "z"
""",
}
for name, text in more.items():
    with open(os.path.join(root, name), "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
more = {
"R21_BlockHooks.feature": """Feature: R21 A block hook on a nested step

@r6-block-throws
Scenario: R21 one, BeforeScenarioBlock throws as a nested When starts
  Given r6 composite runs a when a block hook stops with a table
  Then an r step "skipped"

@r6-block-runs
Scenario: R21 two, BeforeScenarioBlock runs a step as a nested When starts
  Given r6 composite runs a when a block hook adds to
  Then an r step "after"
""",
}
for name, text in more.items():
    with open(os.path.join(root, name), "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
more = {
"R22_SameTextTwice.feature": """Feature: R22 The same nested text twice, a hook stops the second

@r6-stop-second
Scenario: R22 one
  Given r6 composite runs the same nested text twice with a doc each
""",
}
for name, text in more.items():
    with open(os.path.join(root, name), "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
