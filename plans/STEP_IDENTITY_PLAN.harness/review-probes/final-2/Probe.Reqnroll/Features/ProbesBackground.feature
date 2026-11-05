Feature: Probes with a Background

  Background:
    Given a registered customer
    And the shop is ready

  Scenario: W0 control
    When the customer places the order
    Then the order is confirmed

  @seeded
  Scenario: W1 a hook's step method
    When the customer places the order
    Then the order is confirmed

  @seeded @failgiven
  Scenario: W5 a hook's step method and a Background step fails
    When the customer places the order
    Then the order is confirmed

  @q23
  Scenario: W23 a block hook's step method
    When the customer places the order
    Then the order is confirmed

  @seededfile
  Scenario: W24 a hook's step method with a file
    When the customer places the order
    Then the order is confirmed

  @seededrow
  Scenario Outline: W6 an outline's rows
    When an order of <amount> is placed
    Then the order is confirmed

    Examples:
      | amount |
      | 10     |
      | 20     |
