Feature: Remote question flow
  As a swarm operator
  I want to answer blocked agent questions in the cockpit
  So that agents can continue without terminal-only interaction

  Scenario: Answer a blocking question from the cockpit dashboard
    Given the cockpit service is running
    When an agent submits a blocking question
    Then the question appears in the cockpit dashboard
    When the operator submits an answer in the cockpit UI
    Then the question status is answered
    And the polling endpoint returns the operator answer
