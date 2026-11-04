@hookstep-before-bg
Feature: HookBg

Background:
  Given a background step

Scenario: HB1 A hook's step method ahead of a shared background
  When I act "hb1"
  Then I check

Scenario: HB2 A hook's step method ahead of a shared background
  When I act "hb2"
  Then I check
