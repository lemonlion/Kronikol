Feature: Rev3

Scenario: Q01 a sync Given binding leaves a woven worker step running
    Given a sync binding leaves a worker running as q01
    When a call named q01-when
    And a call named q01-when-and
    Then a call named q01-then
    And the worker is released

Scenario: Q02 an async Given binding leaves a woven worker step running
    Given an async binding leaves a worker running as q02
    When a call named q02-when
    Then a call named q02-then
    And the worker is released

@no-setup-tracking
Scenario: Q03 a sync Given binding leaves a worker running with TrackDuringSetup false
    Given a sync binding leaves a worker running as q03
    When a call named q03-when
    Then a call named q03-then
    And the worker is released

Scenario: Q04 a Given binding starts a background loop of keyword-free steps
    Given a binding starts a keyword free loop as q04
    When a call named q04-when
    Then a call named q04-then
    And the loop is stopped

Scenario: Q05 a Given binding starts a background loop of Then steps
    Given a binding starts a Then loop as q05
    And a call named q05-given-and
    When a call named q05-when
    Then a call named q05-then
    And the loop is stopped

@woven-given-hook
Scenario: Q06 a woven Given in a BeforeScenario hook then a When
    When a call named q06-when
    Then a call named q06-then

@woven-plain-after-hook
Scenario: Q07 a keyword-free woven step in an AfterScenario hook
    Given a call named q07-given
    When a call named q07-when

@no-setup-tracking @woven-given-hook
Scenario: Q08 a woven Given in a BeforeScenario hook then a Then with TrackDuringSetup false
    Then a call named q08-then

Scenario: Q09 a binding with a manual bypassed sub-step
    Given a binding bypasses a manual sub-step as q09
    When a call named q09-when
    Then a call named q09-then

Scenario: Q10 a binding fails and is the last step
    Given a call named q10-given
    When a call named q10-when
    Then a call named q10-then

Scenario: Q11 a Given with no calls then a When that draws its own boundary after a call
    Given nothing happens
    When a binding calls then marks the boundary as q11
    Then a call named q11-then

Scenario: Q12 a Given with a call then a When that draws its own boundary after a call
    Given a call named q12-given
    When a binding calls then marks the boundary as q12
    Then a call named q12-then
