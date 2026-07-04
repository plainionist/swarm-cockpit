using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using SwarmCockpit.Contracts;
using SwarmCockpit.Service;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsEnvironment("Testing") && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
	builder.WebHost.UseUrls("http://localhost:5959");
	builder.WebHost.UseUrls("http://0.0.0.0:5959");
}

builder.Services.AddSingleton<QuestionRepository>();
builder.Services.AddSingleton<AgentRuntimeRepository>();

var app = builder.Build();

app.MapGet("/", async (QuestionRepository repository, AgentRuntimeRepository runtimeRepository, IConfiguration configuration, CancellationToken cancellationToken) =>
{
	var questions = await repository.GetQuestionsAsync(cancellationToken);
	var statuses = await AgentStatusBuilder.BuildAsync(repository, runtimeRepository, configuration, cancellationToken);
	var logsByAgent = new Dictionary<string, IReadOnlyList<AgentLogLineViewModel>>(StringComparer.OrdinalIgnoreCase);
	foreach (var agent in statuses.Select(a => a.AgentName))
	{
		logsByAgent[agent] = await runtimeRepository.GetRecentLogsAsync(agent, 300, cancellationToken);
	}

	var html = HtmlRenderer.RenderDashboard(questions, statuses, logsByAgent);
	return Results.Content(html, "text/html");
});

app.MapPost("/api/agents/{agentName}/logs", async Task<Results<Ok<AgentLogLineViewModel>, BadRequest<string>>>
	(string agentName, IngestAgentLogRequest request, AgentRuntimeRepository repository, CancellationToken cancellationToken) =>
{
	if (string.IsNullOrWhiteSpace(agentName))
	{
		return TypedResults.BadRequest("agentName is required.");
	}

	if (string.IsNullOrWhiteSpace(request.Message))
	{
		return TypedResults.BadRequest("Message is required.");
	}

	var line = await repository.AppendLogAsync(agentName, request.Message, request.Stream, cancellationToken);
	return TypedResults.Ok(line);
});

app.MapGet("/api/agents/{agentName}/logs", async (string agentName, int? take, AgentRuntimeRepository repository, CancellationToken cancellationToken) =>
{
	var lines = await repository.GetRecentLogsAsync(agentName, take ?? 200, cancellationToken);
	return Results.Ok(lines);
});

app.MapDelete("/api/agents/{agentName}/logs", async (string agentName, AgentRuntimeRepository repository, CancellationToken cancellationToken) =>
{
	var deleted = await repository.ClearLogsAsync(agentName, cancellationToken);
	return Results.Ok(new { deleted });
});

app.MapGet("/api/agents/{agentName}/logs/stream", async (HttpContext context, string agentName, long? afterId, AgentRuntimeRepository repository) =>
{
	context.Response.Headers.CacheControl = "no-cache";
	context.Response.Headers.Connection = "keep-alive";
	context.Response.ContentType = "text/event-stream";

	var cancellationToken = context.RequestAborted;
	var cursor = Math.Max(afterId ?? 0, 0);

	while (!cancellationToken.IsCancellationRequested)
	{
		var rows = await repository.GetLogsAfterAsync(agentName, cursor, 100, cancellationToken);
		foreach (var row in rows)
		{
			var payload = JsonSerializer.Serialize(row);
			await context.Response.WriteAsync($"id: {row.Id}\n", cancellationToken);
			await context.Response.WriteAsync("event: log\n", cancellationToken);
			await context.Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
			await context.Response.Body.FlushAsync(cancellationToken);
			cursor = row.Id;
		}

		await Task.Delay(1000, cancellationToken);
	}

	return Results.Empty;
});

app.MapGet("/api/agents/status", async (QuestionRepository questionRepository, AgentRuntimeRepository runtimeRepository, IConfiguration configuration, CancellationToken cancellationToken) =>
{
	var statuses = await AgentStatusBuilder.BuildAsync(questionRepository, runtimeRepository, configuration, cancellationToken);
	return Results.Ok(new SwarmOverviewViewModel(statuses));
});

app.MapPost("/questions/{id}/answer", async Task<Results<RedirectHttpResult, NotFound, BadRequest<string>>>
	(string id, HttpRequest request, QuestionRepository repository, CancellationToken cancellationToken) =>
{
	var form = await request.ReadFormAsync(cancellationToken);
	var answer = form["answer"].ToString();
	if (string.IsNullOrWhiteSpace(answer))
	{
		return TypedResults.BadRequest("Answer is required.");
	}

	var answered = await repository.AnswerQuestionAsync(id, answer, cancellationToken);
	if (!answered)
	{
		return TypedResults.NotFound();
	}

	return TypedResults.Redirect("/");
});

app.MapPost("/api/questions", async Task<Results<Ok<CreateQuestionResponse>, BadRequest<string>>>
	(CreateQuestionRequest request, QuestionRepository repository, CancellationToken cancellationToken) =>
{
	if (string.IsNullOrWhiteSpace(request.AskingAgent)
		|| string.IsNullOrWhiteSpace(request.Context)
		|| string.IsNullOrWhiteSpace(request.Question))
	{
		return TypedResults.BadRequest("AskingAgent, Context, and Question are required.");
	}

	var question = await repository.CreateQuestionAsync(request, cancellationToken);
	return TypedResults.Ok(new CreateQuestionResponse(question.Id));
});

app.MapGet("/api/questions", async (QuestionRepository repository, CancellationToken cancellationToken) =>
{
	var questions = await repository.GetQuestionsAsync(cancellationToken);
	return Results.Ok(questions);
});

app.MapGet("/api/questions/{id}", async Task<Results<Ok<QuestionViewModel>, NotFound>>
	(string id, QuestionRepository repository, CancellationToken cancellationToken) =>
{
	var question = await repository.GetQuestionAsync(id, cancellationToken);
	if (question is null)
	{
		return TypedResults.NotFound();
	}

	return TypedResults.Ok(question);
});

app.MapPost("/api/questions/{id}/answer", async Task<Results<Ok<QuestionViewModel>, NotFound, BadRequest<string>>>
	(string id, AnswerQuestionRequest request, QuestionRepository repository, CancellationToken cancellationToken) =>
{
	if (string.IsNullOrWhiteSpace(request.Answer))
	{
		return TypedResults.BadRequest("Answer is required.");
	}

	var answered = await repository.AnswerQuestionAsync(id, request.Answer, cancellationToken);
	if (!answered)
	{
		return TypedResults.NotFound();
	}

	var updated = await repository.GetQuestionAsync(id, cancellationToken);
	return TypedResults.Ok(updated!);
});

app.MapGet("/api/questions/{id}/answer", async (string id, QuestionRepository repository, CancellationToken cancellationToken) =>
{
	var question = await repository.GetQuestionAsync(id, cancellationToken);
	if (question is null)
	{
		return Results.NotFound();
	}

	if (!string.Equals(question.Status, "answered", StringComparison.OrdinalIgnoreCase))
	{
		return Results.Json(new PollAnswerResponse("open", null), statusCode: (int)HttpStatusCode.Accepted);
	}

	return Results.Ok(new PollAnswerResponse("answered", question.Answer));
});

app.Run();

public partial class Program;
