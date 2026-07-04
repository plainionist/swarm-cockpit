using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using SwarmCockpit.Contracts;

namespace SwarmCockpit.AcceptanceTests;

public sealed class RemoteQuestionFlowAcceptanceTests : IClassFixture<RemoteQuestionFlowAcceptanceTests.CockpitFactory>
{
    private readonly HttpClient _client;

    public RemoteQuestionFlowAcceptanceTests(CockpitFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact(DisplayName = "Scenario: operator answers a blocking question through the cockpit UI")]
    public async Task OperatorCanAnswerBlockingQuestionAndAgentCanPollAnswer()
    {
        var createRequest = new CreateQuestionRequest(
            AskingAgent: "Implementer",
            Context: "Need decision for storage format",
            Question: "Should we keep SQLite for the first slice?",
            Options: ["Yes", "No"],
            Recommendation: "Yes");

        var createResponse = await _client.PostAsJsonAsync("/api/questions", createRequest);
        createResponse.EnsureSuccessStatusCode();
        var createBody = await createResponse.Content.ReadFromJsonAsync<CreateQuestionResponse>();

        Assert.NotNull(createBody);

        var dashboardHtml = await _client.GetStringAsync("/");
        Assert.Contains("Should we keep SQLite for the first slice?", dashboardHtml);

        var pollBeforeAnswer = await _client.GetAsync($"/api/questions/{createBody!.Id}/answer");
        Assert.Equal(HttpStatusCode.Accepted, pollBeforeAnswer.StatusCode);

        using var formContent = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("answer", "Keep SQLite in slice one")
        ]);

        var submitResponse = await _client.PostAsync($"/questions/{createBody.Id}/answer", formContent);
        Assert.Equal(HttpStatusCode.Redirect, submitResponse.StatusCode);

        var pollAfterAnswer = await _client.GetAsync($"/api/questions/{createBody.Id}/answer");
        pollAfterAnswer.EnsureSuccessStatusCode();
        var pollBody = await pollAfterAnswer.Content.ReadFromJsonAsync<PollAnswerResponse>();

        Assert.NotNull(pollBody);
        Assert.Equal("answered", pollBody!.Status);
        Assert.Equal("Keep SQLite in slice one", pollBody.Answer);

        var questionResponse = await _client.GetAsync($"/api/questions/{createBody.Id}");
        questionResponse.EnsureSuccessStatusCode();
        var viewModel = await questionResponse.Content.ReadFromJsonAsync<QuestionViewModel>();

        Assert.NotNull(viewModel);
        Assert.Equal("answered", viewModel!.Status);
        Assert.Equal("Keep SQLite in slice one", viewModel.Answer);
    }

    public sealed class CockpitFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbFile = Path.Combine(Path.GetTempPath(), $"swarm-cockpit-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["Persistence:ConnectionString"] = $"Data Source={_dbFile}"
                };
                config.AddInMemoryCollection(values);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing && File.Exists(_dbFile))
            {
                try
                {
                    File.Delete(_dbFile);
                }
                catch (IOException)
                {
                    // Test process shutdown can still hold an SQLite handle briefly.
                }
            }
        }
    }
}
