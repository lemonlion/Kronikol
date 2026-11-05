Feature: I siblings with no background written

  Scenario: I1 passes, sharing an opening with the rest
    Given a registered customer
    And the shop is ready
    When the customer places the order
    Then the order is confirmed

  Scenario: I2 fails, sharing the opening
    Given a registered customer
    When the payment fails
    Then the order is confirmed

  Scenario: I3 passes too
    Given a registered customer
    And the shop is ready
    When order 3 is placed
    Then the order is confirmed
