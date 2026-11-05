Feature: Backgrounds

Background:
    Given a call named bg-g

Scenario: B01 background given then when
    When a call named b01-w

Scenario: B02 background given and scenario given then when
    Given a call named b02-g
    When a call named b02-w
