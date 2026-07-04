using Microsoft.AspNetCore.Http.HttpResults;
using System.Text.Json;
using SwarmCockpit.Contracts;
using SwarmCockpit.Service;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsEnvironment("Testing") && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
	builder.WebHost.UseUrls("http://localhost:5959");
	builder.WebHost.UseUrls("http://0.0.0.0:5959");
}

builder.Services.AddSingleton<AgentRuntimeRepository>();

var app = builder.Build();

app.MapGet("/", async (AgentRuntimeRepository runtimeRepository, IConfiguration configuration, CancellationToken cancellationToken) =>
{
	var statuses = await AgentStatusBuilder.BuildAsync(runtimeRepository, configuration, cancellationToken);
	var logsByAgent = new Dictionary<string, IReadOnlyList<AgentLogLineViewModel>>(StringComparer.OrdinalIgnoreCase);
	var screensByAgent = new Dictionary<string, AgentScreenViewModel>(StringComparer.OrdinalIgnoreCase);
	foreach (var agent in statuses.Select(a => a.AgentName))
	{
		logsByAgent[agent] = await runtimeRepository.GetRecentLogsAsync(agent, 300, cancellationToken);
		var screen = await runtimeRepository.GetScreenAsync(agent, cancellationToken);
		if (screen is not null)
		{
			screensByAgent[agent] = screen;
		}
	}

	var html = HtmlRenderer.RenderDashboard(statuses, logsByAgent, screensByAgent);
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

app.MapPut("/api/agents/{agentName}/screen", async Task<Results<Ok<AgentScreenViewModel>, BadRequest<string>>>
	(string agentName, HttpRequest request, AgentRuntimeRepository repository, CancellationToken cancellationToken) =>
{
	if (string.IsNullOrWhiteSpace(agentName))
	{
		return TypedResults.BadRequest("agentName is required.");
	}

	string content;
	using (var reader = new StreamReader(request.Body))
	{
		content = await reader.ReadToEndAsync(cancellationToken);
	}

	var screen = await repository.UpsertScreenAsync(agentName, content, cancellationToken);
	return TypedResults.Ok(screen);
});

app.MapGet("/api/agents/{agentName}/screen", async Task<Results<Ok<AgentScreenViewModel>, NotFound>>
	(string agentName, AgentRuntimeRepository repository, CancellationToken cancellationToken) =>
{
	var screen = await repository.GetScreenAsync(agentName, cancellationToken);
	if (screen is null)
	{
		return TypedResults.NotFound();
	}

	return TypedResults.Ok(screen);
});

app.MapPost("/api/agents/{agentName}/input", async Task<Results<Ok<AgentInputViewModel>, BadRequest<string>>>
	(string agentName, SendAgentInputRequest request, AgentRuntimeRepository repository, CancellationToken cancellationToken) =>
{
	if (string.IsNullOrWhiteSpace(agentName))
	{
		return TypedResults.BadRequest("agentName is required.");
	}

	if (request.Text is null)
	{
		return TypedResults.BadRequest("Text is required.");
	}

	var queued = await repository.EnqueueInputAsync(agentName, request.Text, request.Submit, cancellationToken);
	return TypedResults.Ok(queued);
});

// Poller-facing endpoint. Returns one line per pending input as:
//   <id> <submit 0|1> <base64(agentName)> <base64(text)>
// base64 keeps parsing dependency-free in bash (base64 -d only).
app.MapGet("/api/inputs/pending", async (AgentRuntimeRepository repository, CancellationToken cancellationToken) =>
{
	var pending = await repository.GetPendingInputsAsync(cancellationToken);
	var sb = new System.Text.StringBuilder();
	foreach (var input in pending)
	{
		var agentB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(input.AgentName));
		var textB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(input.Text));
		sb.Append(input.Id).Append(' ')
			.Append(input.Submit ? '1' : '0').Append(' ')
			.Append(agentB64).Append(' ')
			.Append(textB64).Append('\n');
	}

	return Results.Text(sb.ToString(), "text/plain");
});

app.MapPost("/api/inputs/{id:long}/delivered", async (long id, AgentRuntimeRepository repository, CancellationToken cancellationToken) =>
{
	var ok = await repository.MarkInputDeliveredAsync(id, cancellationToken);
	return Results.Ok(new { delivered = ok });
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

app.MapGet("/api/agents/status", async (AgentRuntimeRepository runtimeRepository, IConfiguration configuration, CancellationToken cancellationToken) =>
{
	var statuses = await AgentStatusBuilder.BuildAsync(runtimeRepository, configuration, cancellationToken);
	return Results.Ok(new SwarmOverviewViewModel(statuses));
});

app.Run();

public partial class Program;
