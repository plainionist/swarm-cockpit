using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SwarmCockpit.Contracts;

namespace SwarmCockpit.AcceptanceTests;

public sealed class SwarmVisibilityAcceptanceTests : IClassFixture<RemoteQuestionFlowAcceptanceTests.CockpitFactory>
{
    private readonly HttpClient _client;

    public SwarmVisibilityAcceptanceTests(RemoteQuestionFlowAcceptanceTests.CockpitFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact(DisplayName = "Scenario: three-agent board shows running and blocked states from logs and questions")]
    public async Task ThreeAgentBoardShowsRunningAndBlockedStates()
    {
        var ingest = await _client.PostAsJsonAsync(
            "/api/agents/Implementer/logs",
            new IngestAgentLogRequest("Compiling feature slice", "stdout"));
        ingest.EnsureSuccessStatusCode();

        var statusResponse = await _client.GetAsync("/api/agents/status");
        statusResponse.EnsureSuccessStatusCode();
        var overview = await statusResponse.Content.ReadFromJsonAsync<SwarmOverviewViewModel>();

        Assert.NotNull(overview);
        Assert.Equal(3, overview!.Agents.Count(a => a.AgentName is "Architect" or "Implementer" or "Verifier"));

        var implementer = overview.Agents.Single(a => a.AgentName == "Implementer");
        Assert.Equal("running", implementer.Status);
        Assert.Equal("Compiling feature slice", implementer.LastMessage);

        var dashboardHtml = await _client.GetStringAsync("/");
        Assert.Contains("Swarm", dashboardHtml);
        Assert.Contains("Compiling feature slice", dashboardHtml);

        var createQuestion = await _client.PostAsJsonAsync(
            "/api/questions",
            new CreateQuestionRequest(
                AskingAgent: "Implementer",
                Context: "Need product choice",
                Question: "Proceed with A or B?",
                Options: ["A", "B"],
                Recommendation: "A"));
        createQuestion.EnsureSuccessStatusCode();

        var blockedStatusResponse = await _client.GetAsync("/api/agents/status");
        blockedStatusResponse.EnsureSuccessStatusCode();
        var blockedOverview = await blockedStatusResponse.Content.ReadFromJsonAsync<SwarmOverviewViewModel>();

        Assert.NotNull(blockedOverview);
        var blockedImplementer = blockedOverview!.Agents.Single(a => a.AgentName == "Implementer");
        Assert.Equal("blocked", blockedImplementer.Status);
        Assert.True(blockedImplementer.NeedsHumanInput);
    }
}
