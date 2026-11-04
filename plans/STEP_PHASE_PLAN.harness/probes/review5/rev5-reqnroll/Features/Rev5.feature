Feature: Rev5

Scenario: R01 a Then binding that marks the boundary, with a When before it
    Given a call named r01-seed
    When a call named r01-act
    Then a marking check named r01-then

Scenario: R02 a second When binding marks, the first does not
    Given a call named r02-seed
    When a call named r02-act1
    And a marking act named r02-act2
    Then a call named r02-then

@markafter
Scenario: R03 an after-scenario hook marks before its clean-up
    Given a call named r03-seed
    When a call named r03-act
    Then a call named r03-then

@health
Scenario: R04 a before-scenario hook runs a woven Then, a health check
    Given a call named r04-seed
    When a call named r04-act
    Then a call named r04-then

@login
Scenario: R05 a before-scenario hook runs a woven When, a log-in
    Given a call named r05-seed
    When a call named r05-act
    Then a call named r05-then

@poller
Scenario: R06 a hook forks a poller of woven Thens that fires during the Given
    Given a poll-releasing call named r06-seed
    And a call named r06-seed2
    When a call named r06-act
    Then a call named r06-then

@hookgiven
Scenario: R07 a before-scenario hook runs a woven Given, the scenario starts at its When
    When a call named r07-act
    Then a call named r07-then

Scenario: R09 a hand step closed on another flow in a Given binding
    Given a hand step closed on another flow named r09-seed
    When a call named r09-act
    Then a call named r09-then

Scenario: R09b a hand step closed on another flow in a sync Given binding
    Given a hand step closed on another flow in a sync binding named r09b-seed
    When a call named r09b-act
    Then a call named r09b-then

@aftereach
Scenario: R11 an after-step hook runs a woven Then
    Given a call named r11-seed
    And a call named r11-seed2
    When a call named r11-act
    Then a call named r11-then

Scenario: R14 a When binding marks setup for a part of itself
    Given a call named r14-seed
    When a binding that goes back to setup named r14-act
    Then a call named r14-then

Scenario: R15 a Given with a call, the first When has no call, a Then has one
    Given a call named r15-seed
    When nothing is called
    Then a call named r15-then

Scenario: R16 two scenarios share a handler, first
    Given a shared call named r16-seed
    When a shared call named r16-act

Scenario: R17 two scenarios share a handler, second
    Given a shared call named r17-seed
    When a shared call named r17-act

Scenario: R18 an act then a Given then a When, first act with a call
    When a call named r18-act1
    Then a call named r18-check
    Given a call named r18-seed
    When a call named r18-act2

Scenario: R19 a first When with no call then a Given with a call then a When with a call
    When nothing is called
    Given a call named r19-seed
    When a call named r19-act

Scenario: R20 a failing Given, an after-scenario hook with a call
    Given a failing step named r20-seed
    When a call named r20-act

Scenario: R21 a failing nested Gherkin step inside a When
    Given a call named r21-seed
    When a binding runs a failing nested Given named r21-nested
    Then a call named r21-then

Scenario: R22 a nested Gherkin When inside a Given, then a real When
    Given a binding runs a nested When named r22-nested
    When a call named r22-act
    Then a call named r22-then

Scenario Outline: R23 an outline with a nested woven Given
    Given a call named r23-seed-<row>
    When a binding calls a woven Given named r23-<row>
    Then a call named r23-then-<row>
Examples:
    | row |
    | a   |
    | b   |

Scenario: R24 a message only Given, an HTTP When
    Given a tracked event named r24-seed
    When a call named r24-act
