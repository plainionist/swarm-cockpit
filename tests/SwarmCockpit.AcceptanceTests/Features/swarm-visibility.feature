Feature: Swarm visibility
  As an operator
  I want to see what each agent is doing in near real time
  So I can understand progress and detect blocked work

  Scenario: Three-agent status board reflects logs and blocking questions
    Given three agents are configured
    When an agent emits console output
    Then the agent appears as running in the status API
    And the dashboard shows the recent log line
    When the same agent opens a blocking question
    Then the agent appears as blocked in the status API
