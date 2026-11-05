Feature: F steps

  Scenario: F01 a step skips itself
    Given a registered customer
    When a step skips itself
    Then the order is confirmed

  Scenario: F02 a step calls ScenarioContext Pending
    Given a registered customer
    When a step calls ScenarioContext Pending
    Then the order is confirmed

  Scenario: F03 a step is ambiguous
    Given a registered customer
    When this step is ambiguous
    Then the order is confirmed

  Scenario: F04 a step binds badly
    Given a registered customer
    When this step binds badly
    Then the order is confirmed

  Scenario: F05 an ambiguous step after a failure
    When the payment fails
    When this step is ambiguous
    Then the order is confirmed

  Scenario: F06 an undefined step after a failure
    When the payment fails
    When nobody defined this step in F06
    Then the order is confirmed

  Scenario: F07 a badly bound step after a failure
    When the payment fails
    When this step binds badly
    Then the order is confirmed

  Scenario: F08 a pending step after a failure
    When the payment fails
    When a step is pending
    Then the order is confirmed

  Scenario: F09 the same step written twice, failing
    When the payment fails
    When the payment fails
    When the payment fails

  Scenario: F10 undefined, then undefined again, then And
    Given nobody defined this first step in F10
    And nobody defined this second step in F10
    But the order is confirmed

  Scenario: F11 a skipped step with a doc string and a table
    When the payment fails
    Then the amount is 5 with the note
      """
      kept
      """
    Then the basket is listed
      | sku  | count |
      | PEAR | 2     |

  Scenario: F12 a step fails at And keyword rows
    Given a registered customer
    And an inner step fails
    And the shop is ready
    But the shop is stocked
    * the shop is stocked
