Feature: Q14 Two Examples blocks, a Background step attaches a file, a row fails in its Background

Background:
  Given q step attaches a file for its row
  And q step fails for row b

Scenario Outline: Q14 row <row>
  When an r step "<row>"

Examples: first block
  | row |
  | a   |
  | b   |

Examples: second block
  | row |
  | c   |
  | d   |
