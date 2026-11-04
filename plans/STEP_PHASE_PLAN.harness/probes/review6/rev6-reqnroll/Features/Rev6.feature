Feature: Rev6

@hookgivenwhen
Scenario: R41 a before-scenario hook runs a woven Given then a woven When
    Given a call named r41-seed
    When a call named r41-act
    Then a call named r41-then

@hookgivenwhen
Scenario: R42 the same hook, and the When binding marks after a call
    Given a call named r42-seed
    When a binding marks after a call named r42-act
    Then a call named r42-then

@hookwhengiven
Scenario: R43 a before-scenario hook runs a woven When then a woven Given
    Given a call named r43-seed
    When a call named r43-act
    Then a call named r43-then

Scenario: R40 a When leaves a woven step running, a later Then marks
    Given a call named r40-seed
    When a binding leaves a woven Given running named r40-act
    Then a marking check named r40-then
    And the left step is released named r40-release

Scenario: R44 the same with no marking Then
    Given a call named r44-seed
    When a binding leaves a woven Given running named r44-act
    Then a call named r44-then
    And the left step is released named r44-release

Scenario: R45 a When binding bypasses its own step, a later Then marks
    Given a call named r45-seed
    When a binding bypasses its own step named r45-act
    Then a marking check named r45-then

Scenario: R46 a When binding runs skipped woven steps of each stub
    Given a call named r46-seed
    When a binding runs skipped woven steps named r46-act
    And a call named r46-and
    Then a call named r46-then

Scenario: R47 a Given binding runs skipped woven steps of each stub
    Given a binding runs skipped woven steps named r47-seed
    And a call named r47-and
    When a call named r47-act
    Then a call named r47-then

Scenario: R48 a sync When binding marks on a forked flow
    Given a call named r48-seed
    When a binding marks on a fork after a call named r48-act
    Then a call named r48-then

Scenario: R49 a hand step in a sync When binding is bypassed by hand, a Then marks
    Given a call named r49-seed
    When a sync binding bypasses a hand step named r49-act
    Then a marking check named r49-then

Scenario: R50 a retried body in one scenario, the second attempt marks too
    Given a call named r50-seed1
    When a binding marks after a call named r50-act1
    Given a call named r50-seed2
    When a binding marks after a call named r50-act2
    Then a call named r50-then

Scenario: R51 a hand Given closed on another flow in an async binding, then a marking When
    Given a hand step closed on another flow in a sync binding named r51-seed
    When a binding marks after a call named r51-act
    Then a call named r51-then

@hookgetter
Scenario: R52 a hook runs a woven Given whose getter makes a call, then the scenario
    Given a call named r52-seed
    When a call named r52-act
    Then a call named r52-then
