Feature: Hook shapes
    What the adapter's hooks do with each failure a suite's hooks can make.

Background:
    Given bg passes
    And bg runs inner

Scenario: H01 green
    When own passes
    Then own passes too

@after-step-throw
Scenario: H02 AfterStep hook fails own step
    When hook target top
    Then own passes too

@after-step-throw
Scenario: H03 AfterStep hook fails inner step
    When own runs hook target inner

@after-step-tie
Scenario: H04 AfterStep hook tied with the adapter's order fails own step
    When hook target top

@before-step-first
Scenario: H05 BeforeStep hook first fails own step
    When hook target top

@before-step-late
Scenario: H06 BeforeStep hook default order fails own step
    When hook target top

@before-step-late
Scenario: H07 BeforeStep hook default order fails inner step
    When own runs hook target inner

Scenario: H08 pending step
    When a pending step

Scenario: H09 undefined step
    When a step nobody defined

@after-scenario-throw
Scenario: H10 AfterScenario hook fails
    When own passes

@after-step-throw @reads-list-min
Scenario: H11 AfterScenario hook at MinValue reads the list
    When hook target top

@after-step-throw @reads-list-default @attaches-after
Scenario: H12 AfterScenario hook default order reads the list and attaches
    When hook target top

Scenario: H13 inner fails and outer goes on
    When own runs failing inner and goes on

@after-step-runs-step
Scenario: H14 AfterStep hook runs a step
    When hook target top

@before-scenario-runs-step
Scenario: H15 BeforeScenario hook runs a step
    When own passes

Scenario: H16 outer step fails after its inner step passed
    When own runs inner then fails

@before-step-first
Scenario: H17 hook stops second inner step
    When own runs inner then hook target inner

@after-step-throw
Scenario: H18 two inner steps and the first left open
    When own runs hook target inner then another

Scenario: H19 step with a duplicate of the Background's text
    Given bg passes
    When own passes

@after-scenario-runs-step
Scenario: H20 AfterScenario hook runs a step on a green scenario
    When own passes

@after-scenario-runs-failing-step
Scenario: H21 AfterScenario hook runs a failing step on a green scenario
    When own passes

Scenario Outline: H30 outline <row>
    When own passes for <row>

    Examples:
        | row |
        | one |
        | two |

Rule: A rule's Background

    Background:
        Given rule bg passes

    Scenario: H40 in a rule
        When own passes
