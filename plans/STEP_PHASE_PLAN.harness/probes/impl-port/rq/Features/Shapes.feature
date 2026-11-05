Feature: Shapes

Scenario: R01 control
    Given a call named r01-g
    When a call named r01-w
    Then a call named r01-t

Scenario: R02 message only given and when then a call in then
    Given a message named r02-g
    When a message named r02-w
    Then a call named r02-t

Scenario: R03 explicit first in when
    Given a call named r03-g
    When StartAction then a call named r03-w
    Then a call named r03-t

Scenario: R04 explicit after a call in when
    Given a call named r04-g
    When the call r04-prep comes before StartAction and the call r04-w
    Then a call named r04-t

Scenario: R05 explicit last in when
    Given a call named r05-g
    When the call r05-w comes before StartAction
    Then a call named r05-t

Scenario: R06 explicit in then
    Given a call named r06-g
    When a call named r06-w
    Then StartAction then a call named r06-t

@before-block-explicit
Scenario: R07 explicit in BeforeScenarioBlock of when
    Given a call named r07-g
    And a call named r07-g2
    When a call named r07-w
    Then a call named r07-t

@after-step-explicit
Scenario: R08 explicit in AfterStep of when
    Given a call named r08-g
    When a call named r08-w
    Then a call named r08-t

@before-step-explicit
Scenario: R09 explicit in BeforeStep of when
    Given a call named r09-g
    When a call named r09-w
    Then a call named r09-t

@hook-calls
Scenario: R10 hook calls everywhere
    Given a call named r10-g
    When a call named r10-w
    Then a call named r10-t

@hook-calls
Scenario: R11 hook calls and a when first
    When a call named r11-w
    Then a call named r11-t

@scenario-hook-call
Scenario: R12 before scenario call and given with no call
    Given a message named r12-g
    When a call named r12-w

@no-fetcher
Scenario: R13 no step type fetcher and explicit in a later step
    Given a call named r13-g
    When a call named r13-prep
    And StartAction then a call named r13-act
    Then a call named r13-t

@no-fetcher
Scenario: R14 no step type fetcher and no explicit
    Given a call named r14-g
    When a call named r14-w
    Then a call named r14-t

@shared-handler
Scenario: R15 shared handler one
    Given a call named r15-g
    When a call named r15-w

@shared-handler
Scenario: R16 shared handler two
    Given a call named r16-g
    When a call named r16-w

Scenario Outline: R17 outline row <row>
    Given a call named r17-<row>-g
    When a call named r17-<row>-w

    Examples:
        | row |
        | a   |
        | b   |

Scenario: R18 given fails
    Given a call named r18-g
    And a failing step
    When a call named r18-w

Scenario: R19 when fails
    Given a call named r19-g
    When a failing step
    Then a call named r19-t

@no-setup-tracking
Scenario: R20 TrackDuringSetup off
    Given a call named r20-g
    When a call named r20-w
    Then a call named r20-t

@no-action-tracking
Scenario: R21 TrackDuringAction off
    Given a call named r21-g
    When a call named r21-w
    Then a call named r21-t

@given-explicit
Scenario: R22 explicit at the end of a given then when with no call then then
    Given the call r22-g comes before StartAction
    When a message named r22-w
    Then a call named r22-t

Scenario: R23 then only after given
    Given a call named r23-g
    Then a call named r23-t

Scenario: R24 StartSetup in when then StartAction
    Given a call named r24-g
    When StartSetup then a call named r24-arrange then StartAction then a call named r24-act
    Then a call named r24-t

@clear-steps
Scenario: R25 consumer resets the step list in a hook
    Given a call named r25-g
    When a call named r25-w
    Then a call named r25-t

@hand-phase
Scenario: R26 hand written phase on the flow in a given
    Given the phase is written Action by hand then a call named r26-g
    And a call named r26-g2
    When a call named r26-w
    Then a call named r26-t

Scenario: R27 explicit in a later step with no http in when and the default fetcher
    Given a call named r27-g
    When a message named r27-prep
    And StartAction then a call named r27-act
    Then a call named r27-t

Scenario: R28 explicit on the first call of then with no http before
    Given a message named r28-g
    When a message named r28-prep
    Then StartAction then a call named r28-t

Scenario: R29 explicit in the second when step
    Given a call named r29-g
    When a call named r29-w1
    When StartAction then a call named r29-w2

Scenario: R30 explicit after an awaited woven sub-step in when
    Given a call named r30-g
    When an async woven sub-step runs then StartAction then a call named r30-w

Scenario: R31 explicit after a sync woven sub-step in when
    Given a call named r31-g
    When a sync woven sub-step runs then StartAction then a call named r31-w

Scenario: R32 explicit after two woven sub-steps together in when
    Given a call named r32-g
    When two woven sub-steps run together then StartAction then a call named r32-w

Scenario: R33 explicit after a nested gherkin step in when
    Given a call named r33-g
    When a nested gherkin given runs then StartAction then a call named r33-w

Scenario: R34 explicit after a woven sub-step that throws and is caught in when
    Given a call named r34-g
    When a throwing woven sub-step is caught then StartAction then a call named r34-w

Scenario: R35 explicit after an unawaited woven sub-step in when
    Given a call named r35-g
    When an unawaited woven sub-step runs then StartAction then a call named r35-w

@custom-fetcher
Scenario: R36 a custom step type fetcher that says the act is later
    Given a call named r36-g
    When a call named r36-prep
    When the fetcher moves on then a call named r36-act
    Then a call named r36-t
