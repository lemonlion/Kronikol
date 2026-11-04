Feature: Nested

Scenario: N01 a When binding runs a Gherkin Given through ITestRunner
    Given a call named n01-given
    When a When binding runs a nested Gherkin Given, then calls n01
    Then a call named n01-then

Scenario: N02 a Given binding runs a Gherkin When through ITestRunner
    Given a Given binding runs a nested Gherkin When, then calls n02
    And a call named n02-and
    When a call named n02-when
    Then a call named n02-then

Scenario: N03 a When binding runs Gherkin And and But through ITestRunner, then an And
    Given a call named n03-given
    When a When binding runs nested Gherkin And and But, then calls n03
    And a call named n03-and-after
    Then a call named n03-then

Scenario: N04 a nested Gherkin Given whose binding calls a woven GivenStep
    Given a call named n04-given
    When a When binding runs a nested Gherkin Given with a woven sub-step, then calls n04
    Then a call named n04-then

Scenario: N05 a sync When binding blocks on a nested Gherkin Given
    Given a call named n05-given
    When a sync When binding blocks on a nested Gherkin Given, then calls n05
    Then a call named n05-then

Scenario: N06 the first step is a When binding with a nested Gherkin Given
    When a When binding runs a nested Gherkin Given, then calls n06
    Then a call named n06-then

Scenario: N07 a When binding runs a Gherkin When through ITestRunner
    Given a call named n07-given
    When a When binding runs a nested Gherkin When, then calls n07
    Then a call named n07-then

Scenario: N08 a When binding runs a nested Gherkin Given, then a Given step follows
    Given a call named n08-given
    When a When binding runs a nested Gherkin Given, then calls n08
    Given a call named n08-second-given
    When a call named n08-second-when

@after-scenario-when
Scenario: N20 a Given then calls, and a woven When in an AfterScenario hook
    Given a call named n20-given
    And a call named n20-given-and

@after-scenario-when
Scenario: N21 a Given and a When, and a woven When in an AfterScenario hook
    Given a call named n21-given
    When a call named n21-when

Scenario: N30 a Given binding runs a woven When on an unflowed thread under a scope set by hand
    Given a Given binding runs a woven When on an unflowed thread, then calls n30
    And a call named n30-and
    When a call named n30-when
    Then a call named n30-then
