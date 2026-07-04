namespace SwarmCockpit.Contracts;

public sealed record CreateQuestionRequest(
    string AskingAgent,
    string Context,
    string Question,
    IReadOnlyList<string> Options,
    string Recommendation);

public sealed record AnswerQuestionRequest(string Answer);

public sealed record QuestionViewModel(
    string Id,
    string AskingAgent,
    string Context,
    string Question,
    IReadOnlyList<string> Options,
    string Recommendation,
    string Status,
    string? Answer,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AnsweredAt);

public sealed record CreateQuestionResponse(string Id);

public sealed record PollAnswerResponse(string Status, string? Answer);
