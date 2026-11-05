Feature: G outline with a table and a failing row

  @seeded
  Scenario Outline: G1 table row fails
    When the amount <amount> is checked
    Given a step with a table:
      | item   | qty   |
      | <item> | <qty> |
    Then the order is confirmed

    Examples:
      | amount | item | qty |
      | 1      | a    | 2   |
      | 13     | b    | 3   |
      | 2      | c    | 4   |
