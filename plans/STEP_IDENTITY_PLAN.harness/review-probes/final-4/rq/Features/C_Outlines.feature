Feature: C outlines

  Scenario Outline: C1 a failed row with a table, an inline value and a doc string after it
    Given a registered customer
    When the payment <fate>
    Then the basket is listed
      | sku   | count   |
      | <sku> | <count> |
    And order <n> is read back
    And the note says
      """
      note <n>
      """

    Examples:
      | fate   | sku   | count | n |
      | passes | APPLE | 1     | 1 |
      | breaks | PEAR  | 2     | 2 |
      | passes | PLUM  | 3     | 3 |

  Scenario Outline: C2 the failed row comes first
    When the payment <fate>
    Then the basket is listed
      | sku   | count   |
      | <sku> | <count> |

    Examples:
      | fate   | sku   | count |
      | breaks | PEAR  | 2     |
      | passes | APPLE | 1     |

  Scenario Outline: C3 two Examples blocks, one failing
    When the payment <fate>
    Then the basket is listed
      | sku   |
      | <sku> |

    Examples: good
      | fate   | sku   |
      | passes | APPLE |

    Examples: bad
      | fate   | sku  |
      | breaks | PEAR |

  Scenario Outline: C4 a row that names an undefined step
    When the <fate> is undefined for this row
    Then the basket is listed
      | sku   |
      | <sku> |

    Examples:
      | fate | sku   |
      | one  | APPLE |
      | two  | PEAR  |

  Scenario Outline: C5 a table no value feeds, a row failing
    When the payment <fate>
    Then the basket is listed
      | sku   |
      | fixed |

    Examples:
      | fate   |
      | passes |
      | breaks |

  @hookthrows
  Scenario Outline: C6 every row's hook throws
    When the payment <fate>
    Then the basket is listed
      | sku   |
      | <sku> |

    Examples:
      | fate   | sku   |
      | passes | APPLE |
      | passes | PEAR  |

  Scenario Outline: C7 a row with values no step used, a later scalar group
    When the payment <fate>
    Then the basket is listed
      | sku   |
      | <sku> |
    Then order <ExpectedA> is read back
    Then order <ExpectedB> is read back

    Examples:
      | fate   | sku   | ExpectedA | ExpectedB |
      | breaks | APPLE | 1         | 2         |
      | passes | PEAR  | 3         | 4         |
