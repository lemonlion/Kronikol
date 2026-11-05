@stale
Feature: F a suite after-scenario hook throws then a feature hook runs a step

  @hookthrows
  Scenario: F1 suite hook throws
    Given a registered customer
    Then the order is confirmed
