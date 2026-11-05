Feature: Review4

@no-setup-tracking @hand-open-in-hook
Scenario: R40 a hand step opened in BeforeScenario and a When first
    When a call named r40-w
    Then a call named r40-t

@hand-open-in-hook
Scenario: R41 a hand step opened in BeforeScenario with Given When
    Given a call named r41-g
    When a call named r41-w
    Then a call named r41-t

@woven-given-in-hook
Scenario: R42 a woven given in a hook then a when first
    When a call named r42-w
    Then a call named r42-t

@woven-when-in-hook
Scenario: R43 a woven when in a hook then given when
    Given a call named r43-g
    When a call named r43-w
    Then a call named r43-t

Scenario: R44 asterisk steps
    * a call named r44-star1
    When a call named r44-w
    * a call named r44-star2
    Then a call named r44-t
    * a call named r44-star3

@no-setup-tracking
Scenario: R45 and and but after each keyword with TrackDuringSetup off
    Given a call named r45-g
    And a call named r45-g-and
    But a call named r45-g-but
    When a call named r45-w
    And a call named r45-w-and
    But a call named r45-w-but
    Then a call named r45-t
    And a call named r45-t-and
    But a call named r45-t-but

@no-setup-tracking
Scenario: R46 a woven given in a when with TrackDuringSetup off
    Given a call named r46-g
    When a woven given runs inside then a call named r46-w
    Then a call named r46-t

@no-setup-tracking
Scenario: R47 a nested gherkin given in a when with TrackDuringSetup off
    Given a call named r47-g
    When a nested gherkin given then a call named r47-w
    Then a call named r47-t

@no-action-tracking
Scenario: R48 a woven when in a given with TrackDuringAction off
    Given a woven when runs inside then a call named r48-g
    When a call named r48-w
    Then a call named r48-t

Scenario: R49 a nested gherkin when in a given
    Given a nested gherkin when then a call named r49-g
    And a call named r49-g2
    When a call named r49-w

Scenario: R50 a fork outlives the given and lands in the when
    Given a fork outlives this step with a call named r50-fork
    When a call named r50-w
    And the fork ends
    Then a call named r50-t

@no-setup-tracking
Scenario: R51 a fork outlives the given with TrackDuringSetup off
    Given a fork outlives this step with a call named r51-fork
    When a call named r51-w
    And the fork ends
    Then a call named r51-t

Scenario: R52 a hand step left open in a given
    Given a hand step left open with keyword Given then a call named r52-g
    When a call named r52-w
    Then a call named r52-t

Scenario: R53 a hand step left open in a when
    Given a call named r53-g
    When a hand step left open with keyword When then a call named r53-w
    And a call named r53-w2
    Then a call named r53-t

@woven-given-after
Scenario: R54 a woven given in AfterScenario
    Given a call named r54-g
    When a call named r54-w

@woven-when-after-step
Scenario: R55 a woven when in AfterStep
    Given a call named r55-g
    And a call named r55-g2
    When a call named r55-w

@woven-given-before-step
Scenario: R56 a woven given in BeforeStep
    Given a call named r56-g
    When a call named r56-w
    Then a call named r56-t

@no-setup-tracking
Scenario: R57 a call on a forked flow in a when
    Given a call named r57-g
    When a call on a flow forked here named r57-w
