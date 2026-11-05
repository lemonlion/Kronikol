Feature: Phases

Scenario: R01 And with the first HTTP call of the action, after a When that only sends a message
    Given a call named r01-given
    When a message named r01-when-msg
    And a call named r01-and
    Then a call named r01-then

Scenario: R02 But with the first HTTP call of the action, no HTTP call after it
    Given a call named r02-given
    When a message named r02-when-msg
    But a call named r02-but

Scenario: R03 a nested Gherkin Given under a When, with calls either side
    Given a call named r03-given
    When a binding runs a nested Gherkin Given named r03
    Then a call named r03-then

Scenario: R04 a nested Gherkin When under a Given
    Given a binding runs a nested Gherkin When named r04
    And a call named r04-and
    When a call named r04-when

Scenario: R05 nested Gherkin Given first thing in a When, then the When's own calls
    Given a call named r05-given
    When a binding starts with a nested Gherkin Given named r05
    Then a call named r05-then

@hooks
Scenario: R06 tracked calls in every hook of a scenario
    Given a call named r06-given
    And a call named r06-given-and
    When a call named r06-when
    And a call named r06-when-and
    Then a call named r06-then
    And a call named r06-then-and

Scenario: R07 after a scenario that ended in And
    Given a call named r07-given

Scenario: R08 a step argument transformation makes a call
    Given a call named r08-given
    When an order for customer r08-customer is placed
    And an order for customer r08-customer-and is placed
    Then a call named r08-then

Scenario: R09 a binding class whose constructor makes a call, first used in an And after a When
    Given a call named r09-given
    When a call named r09-when
    And a binding class with a calling constructor is used, named r09

@aftercall
Scenario: R10 a failing And after a When, then AfterScenario makes a call
    Given a call named r10-given
    When a call named r10-when
    And a failing call named r10-and
    Then a call named r10-never

Scenario: R11 the bullet keyword
    * a call named r11-first
    When a call named r11-when
    * a call named r11-after-when
    Then a call named r11-then
    * a call named r11-after-then

Scenario Outline: R12 outline row
    Given a call named <g>
    When a call named <w>
    And a call named <a>

    Examples:
      | g         | w         | a         |
      | r12-g-one | r12-w-one | r12-a-one |

Scenario: R13 woven Given in an And after a When, and a woven When in an And after a Given
    Given a call named r13-given
    And a woven When runs in an And, named r13-a
    When a call named r13-when
    And a woven Given runs in an And, named r13-b
    Then a call named r13-then

Scenario: R14 StartSetup by hand in a When binding, then And
    Given a call named r14-given
    When a binding calls StartSetup by hand, named r14
    And a call named r14-and
    Then a call named r14-then

Scenario: R15 StartAction by hand in a Given binding, then And and a Given
    Given a binding calls StartAction by hand, named r15
    And a call named r15-and
    Given a call named r15-second-given
    When a call named r15-when

Scenario: R16 a phase written by hand in a When binding, then And
    Given a call named r16-given
    When a binding writes TestPhaseContext by hand, named r16
    And a call named r16-and

Scenario: R19 a nested Gherkin When under a Given, two calls and a message on one client
    Given a binding runs a nested Gherkin When with two calls named r19
    And a plain call named r19-and
    When a plain call named r19-when

Scenario: R20 a When with no HTTP call, its And has two calls, then a Then with none
    Given a plain call named r20-given
    When a message named r20-when-msg
    And two plain calls named r20-and
    Then a message named r20-then-msg

Scenario: R21 a Given binding leaves a keyword-free hand step open, then a When and a Then
    Given a binding leaves a keyword-free hand step open named r21
    When a call named r21-when
    Then a call named r21-then

Scenario: R22 a sync Given binding leaves a keyword-free hand step open, then a When and a Then
    Given a sync binding leaves a keyword-free hand step open named r22
    When a call named r22-when
    Then a call named r22-then

Scenario: R23 a sync When binding leaves a keyword-free hand step open, then a Given and a Then
    When a sync binding leaves a keyword-free hand step open named r23
    Given a call named r23-given
    Then a call named r23-then

Rule: a rule

Background:
    Given a call named bg-given
    And a call named bg-and

Scenario: R17 a scenario under a background that starts with And
    And a call named r17-first-and
    When a call named r17-when
    And a call named r17-and

Scenario: R18 a background then a When and a But
    When a call named r18-when
    But a call named r18-but
    Then a call named r18-then
    But a call named r18-then-but
