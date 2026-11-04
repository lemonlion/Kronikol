Feature: Q05 An outline, a big Background composite fails by itself for one row

Background:
  Given q big composite fails by itself for row b

Scenario Outline: Q05 row <row>
  When an r step "<row>"

Examples:
  | row |
  | a   |
  | b   |
  | c   |
