@hookstep-before-bg
Feature: HookBgReal

Background:
  Given a background step
  When I act "hbr"

Scenario: HR1 A hook step ahead of a real background
  Then I check

Scenario: HR2 A hook step ahead of a real background
  Then I check again
