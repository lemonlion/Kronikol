Feature: BgFails

Background:
  Given a background step
  When a background step fails when tagged

@bgfails
Scenario: BF1 The background step fails here
  Then I check

Scenario: BF2 The background step passes here
  Then I check
