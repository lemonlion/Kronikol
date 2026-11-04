Feature: BgVaries

Background:
  Given a background step that varies
  When I act "v"

Scenario: BV1 The first scenario of the feature
  Then I check

@more-subs
Scenario: BV2 This scenario runs one more sub step in the shared background
  Then I check again
