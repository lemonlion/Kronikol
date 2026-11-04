Feature: Bg

Background:
  Given a background step
  And a second background step

Scenario: B1 Baseline one
  When I act "one"
  Then I check

Scenario: B2 Baseline two
  When I act "two"
  Then I check
