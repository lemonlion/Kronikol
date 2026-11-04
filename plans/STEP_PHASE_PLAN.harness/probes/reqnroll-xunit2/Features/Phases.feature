Feature: Phases

Scenario: R01 async Given sub-step in a When step (the issue)
    Given a call named r01-given
    When a When step calls an async Given sub-step, then calls r01
    Then a call named r01-then

Scenario: R02 sync Given sub-step in a When step
    Given a call named r02-given
    When a When step calls a sync Given sub-step, then calls r02
    Then a call named r02-then

Scenario: R03 async When sub-step in a Given step
    Given a Given step calls an async When sub-step, then calls r03
    And a call named r03-and
    When a call named r03-when
    Then a call named r03-then

Scenario: R04 async Then sub-step in a Given step
    Given a Given step calls an async Then sub-step, then calls r04
    When a call named r04-when

Scenario: R05 keyword-free sub-step in a When step
    Given a call named r05-given
    When a When step calls a keyword-free sub-step, then calls r05
    Then a call named r05-then

Scenario: R06 And and But after each keyword (#195)
    Given a call named r06-given
    And a call named r06-given-and
    But a call named r06-given-but
    When a call named r06-when
    And a call named r06-when-and
    But a call named r06-when-but
    Then a call named r06-then
    And a call named r06-then-and
    But a call named r06-then-but

Scenario: R07 star keyword
    * a call named r07-star1
    Given a call named r07-given
    * a call named r07-star2
    When a call named r07-when
    * a call named r07-star3

@no-setup-tracking
Scenario: R08 TrackDuringSetup false
    Given a call named r08-given
    When a When step calls an async Given sub-step, then calls r08
    Then a call named r08-then

Scenario: R09 a When binding woven as a GivenStep
    Given a call named r09-given
    When a When binding woven as a GivenStep calls r09
    Then a call named r09-then

Scenario: R10 ButWhen sub-step in a Given step
    Given a Given step calls a ButWhen sub-step, then calls r10
    When a call named r10-when

@shared-handler
Scenario: R11 first scenario on a shared handler
    Given a call named r11-given
    When a call named r11-when

@shared-handler
Scenario: R12 second scenario on a shared handler
    Given a call named r12-given
    When a call named r12-when

Scenario: R13 When with no Given
    When a call named r13-when
    Then a call named r13-then

Scenario: R14 caught failure of a Given sub-step in a When step
    Given a call named r14-given
    When a When step catches a failing Given sub-step, then calls r14

@hook-step
Scenario: R15 a woven Given step in a BeforeScenario hook
    When a call named r15-when
    Then a call named r15-then

Scenario: R16 a When sub-step in a keyword-free sub-step in a Given step
    Given a Given step nests When under a keyword-free sub-step, then calls r16
    When a call named r16-when

@hook-call
Scenario: R17 calls in BeforeScenario and AfterScenario hooks
    Given a call named r17-given
    When a call named r17-when

Scenario: R18 a Given with no HTTP call
    Given a message named r18-given-message
    When a call named r18-when

Scenario: R19 a When with no HTTP call
    Given a call named r19-given
    When a message named r19-when-message
    Then a call named r19-then

Scenario: R20 a When with no HTTP call, then an And
    Given a call named r20-given
    When a message named r20-when-message
    And a call named r20-when-and
    Then a call named r20-then

Scenario: R00 the issue's own scenario
    Given a call named r00-given
    When the customer applies a discount through a Given sub-step
    Then a call named r00-then

@no-step-attributes
Scenario: R21 WhenTriggersAction false
    Given a Given step calls an async When sub-step, then calls r21
    When a When step calls an async Given sub-step, then calls r21b
    And a call named r21-and
