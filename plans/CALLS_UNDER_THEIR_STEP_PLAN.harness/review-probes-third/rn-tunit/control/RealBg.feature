Feature: RealBg

Background:
  Given a background step
  When I act "bg"

Scenario: RB1 Shares a background
  Then I check

Scenario: RB2 Shares a background
  Then I check again

Scenario: RB3 Shares a background and has more
  Then I check
  And I check again
