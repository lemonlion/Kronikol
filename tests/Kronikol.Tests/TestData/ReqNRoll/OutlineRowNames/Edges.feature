Feature: Edges

  The cases a filled row name has to get right. Every step is a note, so every row passes.

  Scenario Outline: E01 a title without a placeholder
    Given note <a>

    Examples:
      | a |
      | 1 |
      | 2 |

  Scenario Outline: E02 <a> then <b>, a value that names a later column
    Given note <a> <b>

    Examples:
      | a   | b |
      | <b> | x |
      | y   | z |

  Scenario Outline: E03 weighs <count> on <missing>
    Given note <count>

    Examples:
      | count |
      | 1     |
      | 2     |

  Scenario Outline: E04 the <fruit name> in <größe> and <class>
    Given note <fruit name> <größe> <class>

    Examples:
      | fruit name | größe | class |
      | apple      | klein | A     |
      | pear       | groß  | B     |

  Scenario Outline: E05 <fruit> by block
    Given note <fruit>

    @first
    Examples: First block
      | fruit |
      | apple |

    @second
    Examples: Second block
      | fruit |
      | pear  |

  Scenario Outline: E06 the same name for <fruit>
    Given note <fruit> <n>

    Examples:
      | fruit | n |
      | apple | 1 |
      | apple | 2 |

  Scenario Outline: E07 escapes [<v>]
    Given note <v>

    Examples:
      | v           |
      | pipe \| bar |
      | back \\ sl  |
      | line\nbreak |
      |             |

  Scenario Outline: E08 markup <v>
    Given note <v>

    Examples:
      | v                  |
      | <b>bold</b> & co   |
      | "quoted" 'single'  |

  Scenario: E09 a plain scenario that writes <x> in its title
    Given note plain

  Rule: Rows under a rule

    Scenario Outline: E10 <fruit> under a rule
      Given note <fruit>

      Examples:
        | fruit |
        | apple |
        | pear  |

  Scenario Outline: E11 <a> and <a> again
    Given note <a>

    Examples:
      | a |
      | 1 |
      | 2 |

  Scenario Outline: <fruit> starts E12
    Given note <fruit>

    Examples:
      | fruit |
      | apple |
      | Pear  |

  Scenario Outline: E13 <v>
    Given note <v>

    Examples:
      | v                                                                                                                                                                                                                                                                                                              |
      | a long value: Lorem-free filler text that keeps going past two hundred characters so that any cut, wrap or budget a reader applies to a name is reached by this row of the outline and shows up in what the reader prints, which is what this row is for, nothing more |
      | short |
