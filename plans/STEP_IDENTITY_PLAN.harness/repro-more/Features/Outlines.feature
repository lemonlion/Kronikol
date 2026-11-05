Feature: Outlines

  @seeded
  Scenario Outline: E01 an outline with a hook step on every row
    Given a registered customer
    When an order of <amount> is placed
    Then the order is confirmed

    Examples:
      | amount |
      | 10     |
      | 20     |

  @seededrow
  Scenario Outline: E02 an outline with a hook step on one row
    Given a registered customer
    When an order of <amount> is placed
    Then the order is confirmed

    Examples:
      | amount |
      | 10     |
      | 20     |
