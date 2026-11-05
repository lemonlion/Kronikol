Feature: BG13 a file on a hook's row, in every scenario of a feature with a shared opening

  @seededwithfile
  Scenario: BG13a
    Given a registered customer
    And an order of 131 is placed
    Then the order is confirmed

  @seededwithfile
  Scenario: BG13b
    Given a registered customer
    And an order of 132 is placed
    Then the order is confirmed
