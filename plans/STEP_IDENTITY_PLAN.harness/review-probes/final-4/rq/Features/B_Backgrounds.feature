Feature: B background fails

  Background:
    Given the shop is ready
    And the shop is stocked

  Scenario: B1 passes
    When the customer places the order
    Then the order is confirmed

  @failgiven
  Scenario: B2 the first background step fails
    When the customer places the order
    Then the order is confirmed

  Scenario: B3 writes a background step again after a failure
    When the payment fails
    Given the shop is ready
    And the shop is stocked

  @hookthrows
  Scenario: B4 a plain hook throws
    When the customer places the order

  @hookseedfails
  Scenario: B5 a hook step fails
    When the customer places the order

  @hookskip
  Scenario: B6 a hook skips the scenario
    When the customer places the order

  @afterstep @failgiven
  Scenario: B7 the background fails and an after hook has a step
    When the customer places the order
