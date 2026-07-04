using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using SwarmCockpit.Contracts;

namespace SwarmCockpit.AcceptanceTests;

public sealed class OperatorInputAcceptanceTests : IClassFixture<CockpitFactory>
{
    private readonly HttpClient _client;

    public OperatorInputAcceptanceTests(CockpitFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact(DisplayName = "Scenario: operator input is queued, exposed to the poller, and can be acknowledged")]
    public async Task OperatorInputRoundTripsThroughPollerEndpoints()
    {
        var text = "use option A";

        var enqueue = await _client.PostAsJsonAsync(
            "/api/agents/Architect/input",
            new SendAgentInputRequest(text, true));
        enqueue.EnsureSuccessStatusCode();

        var queued = await enqueue.Content.ReadFromJsonAsync<AgentInputViewModel>();
        Assert.NotNull(queued);
        Assert.Equal("Architect", queued!.AgentName);
        Assert.Equal(text, queued.Text);
        Assert.True(queued.Submit);

        // Poller-facing endpoint: "<id> <submit> <base64 agent> <base64 text>".
        var pending = await _client.GetStringAsync("/api/inputs/pending");
        var line = pending
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Single(l => l.StartsWith($"{queued.Id} "));

        var parts = line.Split(' ');
        Assert.Equal("1", parts[1]);
        Assert.Equal("Architect", Encoding.UTF8.GetString(Convert.FromBase64String(parts[2])));
        Assert.Equal(text, Encoding.UTF8.GetString(Convert.FromBase64String(parts[3])));

        var ack = await _client.PostAsync($"/api/inputs/{queued.Id}/delivered", null);
        ack.EnsureSuccessStatusCode();

        // Once delivered it must not appear as pending again.
        var pendingAfter = await _client.GetStringAsync("/api/inputs/pending");
        Assert.DoesNotContain($"{queued.Id} ", pendingAfter);
    }
}
