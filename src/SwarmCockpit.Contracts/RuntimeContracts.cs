namespace SwarmCockpit.Contracts;

public sealed record IngestAgentLogRequest(string Message, string Stream = "stdout");

public sealed record AgentLogLineViewModel(
    long Id,
    string AgentName,
    string Message,
    string Stream,
    DateTimeOffset CreatedAt);

public sealed record IngestAgentScreenRequest(string Content);

public sealed record SendAgentInputRequest(string Text, bool Submit = true);

public sealed record AgentInputViewModel(
    long Id,
    string AgentName,
    string Text,
    bool Submit,
    DateTimeOffset CreatedAt);

public sealed record AgentScreenViewModel(
    string AgentName,
    string Content,
    DateTimeOffset CapturedAt);

public sealed record AgentStatusViewModel(
    string AgentName,
    string Status,
    DateTimeOffset? LastActivity,
    string? LastMessage,
    bool NeedsHumanInput);

public sealed record SwarmOverviewViewModel(IReadOnlyList<AgentStatusViewModel> Agents);
