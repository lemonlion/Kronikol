Feature: Review

Scenario: Orphan a background task runs a sub-step after its Gherkin step ended
    Given a background task will run a sub-step
    When a long step runs
    Then the result is checked

Scenario: Consumer a loop started in the Given handles messages from later steps
    Given the service is started
    When a message is published
    Then a second message is published

Scenario: Failing a failing step with passed sub-steps
    When a step runs a sub-step then fails

Scenario: Control three plain steps
    Given a plain given
    When a plain when
    Then the result is checked

@userhook
Scenario: Userhook an async BeforeStep hook opens a step and an AfterStep hook closes it
    Given a plain given
    When a plain when

Scenario: Nested a step definition runs another Gherkin step
    Given a step that calls a step
    When a plain when

@synchook
Scenario: Zsynchook a sync hook opens a step by hand and leaves it open for the scenario
    Given a plain given
    When a plain when
