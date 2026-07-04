namespace SwarmCockpit.Contracts;

public sealed record IngestAgentLogRequest(string Message, string Stream = "stdout");

public sealed record AgentLogLineViewModel(
    long Id,
    string AgentName,
    string Message,
    string Stream,
    DateTimeOffset CreatedAt);

public sealed record AgentStatusViewModel(
    string AgentName,
    string Status,
    DateTimeOffset? LastActivity,
    string? LastMessage,
    bool NeedsHumanInput);

public sealed record SwarmOverviewViewModel(IReadOnlyList<AgentStatusViewModel> Agents);
