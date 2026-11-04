Feature: Q02 A keyword woven step reads like the nested step after it, and the nested step fails

Scenario: Q02 one
  Given q composite runs a keyword woven namesake and then a failing step
  Then an r step "not reached"
