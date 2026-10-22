Feature: Baskets

  Scenario Outline: A basket is weighed on the UK scale (<case>)
    Given a basket holding <count> apples
    Then the basket weighs <grams> grams

    Examples:
      | case         | count | grams |
      | empty basket | 0     | 0     |
      | one apple    | 1     | 150   |
      | two apples   | 2     | 301   |
