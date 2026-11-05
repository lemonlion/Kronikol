@lifecycle
Feature: Lifecycle

  Scenario: E04 a feature hook with a step ran before this
    Given a registered customer
    Then the order is confirmed

  Scenario: E05 a binding constructor runs a step
    Given a step whose binding constructor runs a step
    Then the order is confirmed

  Scenario: E06 an argument transformation runs a step
    Given a registered customer
    When a transformed value of 7 is used
    Then the order is confirmed

  @throwbeforestep
  Scenario: E07 a BeforeStep hook throws on the second step
    Given a registered customer
    When an order of 70 is placed
    Then the order is confirmed

  Scenario: E08 a pending step
    Given a registered customer
    When a pending step runs
    Then the order is confirmed

  @lastseed
  Scenario: E09 a step in an AfterScenario hook that ties with Kronikol
    Given a registered customer
    Then the order is confirmed

  Scenario: E13 a call outlasts its step
    Given a call that outlasts its step
    Then the call is answered
    Then the order is confirmed

  Scenario: E14 phases over a repeated When
    Given a registered customer
    When an order of 90 is placed
    And an order of 91 is placed
    Then the order is confirmed
