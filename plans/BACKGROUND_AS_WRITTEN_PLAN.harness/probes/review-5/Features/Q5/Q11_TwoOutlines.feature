Feature: Q11 Two outlines and a rule, several on one page

Background:
  Given an r step that fails for row b

Scenario Outline: Q11 first row <row>
  When an r step "<row>"

Examples:
  | row |
  | a   |
  | b   |

Scenario Outline: Q11 second row <row>
  When an r step "<row>"

Examples:
  | row |
  | b   |
  | a   |

Rule: Q11 rule
  Background:
    Given an r step "in the rule"

  Scenario Outline: Q11 ruled row <row>
    When an r step "<row>"

    Examples:
      | row |
      | a   |
      | b   |
