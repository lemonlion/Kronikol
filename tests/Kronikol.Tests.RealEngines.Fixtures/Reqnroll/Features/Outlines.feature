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

  Scenario Outline: E21 a row that fails groups its values by the table of a step that did not run
    Given a registered customer
    When the payment <fate>
    Given the basket is listed
      | sku   | count   |
      | <sku> | <count> |

    Examples:
      | fate   | sku   | count |
      | passes | APPLE | 1     |
      | fails  | PEAR  | 2     |
