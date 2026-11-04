Feature: Q06 An outline whose first row fails in its Background, each row's nested step names it

Background:
  Given q composite names its row
  And q step fails for row a

Scenario Outline: Q06 row <row>
  When an r step "<row>"

Examples:
  | row |
  | a   |
  | b   |
  | c   |
