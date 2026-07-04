using SwarmCockpit.Contracts;

namespace SwarmCockpit.Service;

internal static class AgentStatusBuilder
{
    public static async Task<IReadOnlyList<AgentStatusViewModel>> BuildAsync(
        QuestionRepository questionRepository,
        AgentRuntimeRepository runtimeRepository,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var questions = await questionRepository.GetQuestionsAsync(cancellationToken);
        var openQuestionAgents = questions
            .Where(q => q.Status.Equals("open", StringComparison.OrdinalIgnoreCase))
            .Select(q => q.AskingAgent)
            .Where(agent => !string.IsNullOrWhiteSpace(agent))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var configuredAgents = configuration
            .GetSection("Swarm:Agents")
            .Get<string[]>()
            ?.Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? new List<string>();

        if (configuredAgents.Count == 0)
        {
            configuredAgents = ["Architect", "Implementer", "Verifier"];
        }

        var runningThresholdSeconds = configuration.GetValue<int?>("Swarm:RunningThresholdSeconds") ?? 45;
        var snapshots = await runtimeRepository.GetLatestAgentActivityAsync(cancellationToken);
        var snapshotByAgent = snapshots.ToDictionary(s => s.AgentName, StringComparer.OrdinalIgnoreCase);

        var agentNames = configuredAgents
            .Concat(snapshotByAgent.Keys)
            .Concat(openQuestionAgents)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var configuredOrder = configuredAgents
            .Select((name, index) => new { name, index })
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        var statuses = new List<AgentStatusViewModel>();
        foreach (var agentName in agentNames)
        {
            snapshotByAgent.TryGetValue(agentName, out var snapshot);
            var needsHumanInput = openQuestionAgents.Contains(agentName);

            var status = "idle";
            if (needsHumanInput)
            {
                status = "blocked";
            }
            else if (snapshot is not null && now - snapshot.LastActivity <= TimeSpan.FromSeconds(runningThresholdSeconds))
            {
                status = "running";
            }

            statuses.Add(new AgentStatusViewModel(
                agentName,
                status,
                snapshot?.LastActivity,
                snapshot?.LastMessage,
                needsHumanInput));
        }

        return statuses
            .OrderBy(status => configuredOrder.TryGetValue(status.AgentName, out var index) ? index : int.MaxValue)
            .ThenBy(status => status.AgentName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
