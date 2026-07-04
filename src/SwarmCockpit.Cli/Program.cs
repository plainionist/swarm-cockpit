using System.Net.Http.Json;
using SwarmCockpit.Contracts;

var parseResult = CliArguments.Parse(args);
if (!parseResult.Success)
{
	Console.Error.WriteLine(parseResult.ErrorMessage);
	Console.Error.WriteLine();
	Console.Error.WriteLine(CliArguments.Usage);
	return 1;
}

var options = parseResult.Options!;

using var httpClient = new HttpClient { BaseAddress = new Uri(options.BaseUrl) };

try
{
	var createRequest = new CreateQuestionRequest(
		options.Agent,
		options.Context,
		options.Question,
		options.Choices,
		options.Recommendation);

	var createResponse = await httpClient.PostAsJsonAsync("/api/questions", createRequest);
	if (!createResponse.IsSuccessStatusCode)
	{
		Console.Error.WriteLine($"Failed to create question. HTTP {(int)createResponse.StatusCode} {createResponse.ReasonPhrase}");
		return 2;
	}

	var createBody = await createResponse.Content.ReadFromJsonAsync<CreateQuestionResponse>();
	if (createBody is null || string.IsNullOrWhiteSpace(createBody.Id))
	{
		Console.Error.WriteLine("Failed to parse create-question response.");
		return 2;
	}

	var questionUrl = new Uri(httpClient.BaseAddress!, "/").ToString();
	Console.Error.WriteLine($"Question created: {createBody.Id}");
	Console.Error.WriteLine($"Open in browser: {questionUrl}");
	Console.Error.WriteLine("Waiting for answer...");

	while (true)
	{
		var pollResponse = await httpClient.GetAsync($"/api/questions/{createBody.Id}/answer");
		if ((int)pollResponse.StatusCode == 202)
		{
			await Task.Delay(options.PollIntervalMs);
			continue;
		}

		if (!pollResponse.IsSuccessStatusCode)
		{
			Console.Error.WriteLine($"Polling failed. HTTP {(int)pollResponse.StatusCode} {pollResponse.ReasonPhrase}");
			return 2;
		}

		var pollBody = await pollResponse.Content.ReadFromJsonAsync<PollAnswerResponse>();
		if (pollBody is null)
		{
			Console.Error.WriteLine("Failed to parse poll response.");
			return 2;
		}

		if (!string.Equals(pollBody.Status, "answered", StringComparison.OrdinalIgnoreCase)
			|| string.IsNullOrWhiteSpace(pollBody.Answer))
		{
			await Task.Delay(options.PollIntervalMs);
			continue;
		}

		Console.WriteLine(pollBody.Answer);
		return 0;
	}
}
catch (HttpRequestException ex)
{
	Console.Error.WriteLine($"Cannot reach Swarm Cockpit service at {options.BaseUrl}. {ex.Message}");
	return 2;
}

internal sealed record CliOptions(
	string BaseUrl,
	string Agent,
	string Context,
	string Question,
	IReadOnlyList<string> Choices,
	string Recommendation,
	int PollIntervalMs);

internal static class CliArguments
{
	public const string Usage =
		"""
		Usage:
		  swarm-ask --agent <name> --context <text> --question <text> --recommendation <text> [--option <text> ...] [--base-url <url>] [--poll-interval-ms <ms>]
		""";

	public static (bool Success, CliOptions? Options, string ErrorMessage) Parse(string[] args)
	{
		var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
		{
			["--base-url"] = new(),
			["--agent"] = new(),
			["--context"] = new(),
			["--question"] = new(),
			["--option"] = new(),
			["--recommendation"] = new(),
			["--poll-interval-ms"] = new()
		};

		for (var i = 0; i < args.Length; i++)
		{
			var key = args[i];
			if (!values.ContainsKey(key))
			{
				return (false, null, $"Unknown argument: {key}");
			}

			if (i + 1 >= args.Length)
			{
				return (false, null, $"Missing value for argument: {key}");
			}

			values[key].Add(args[++i]);
		}

		string? GetSingle(string key) => values[key].LastOrDefault();

		var baseUrl = GetSingle("--base-url") ?? "http://localhost:5959";
		var agent = GetSingle("--agent");
		var context = GetSingle("--context");
		var question = GetSingle("--question");
		var recommendation = GetSingle("--recommendation");
		var pollIntervalRaw = GetSingle("--poll-interval-ms") ?? "1000";

		if (string.IsNullOrWhiteSpace(agent)
			|| string.IsNullOrWhiteSpace(context)
			|| string.IsNullOrWhiteSpace(question)
			|| string.IsNullOrWhiteSpace(recommendation))
		{
			return (false, null, "Missing required arguments.");
		}

		if (!int.TryParse(pollIntervalRaw, out var pollIntervalMs) || pollIntervalMs < 100)
		{
			return (false, null, "poll-interval-ms must be an integer >= 100.");
		}

		var choices = values["--option"].Where(v => !string.IsNullOrWhiteSpace(v)).ToList();

		return (
			true,
			new CliOptions(baseUrl, agent.Trim(), context.Trim(), question.Trim(), choices, recommendation.Trim(), pollIntervalMs),
			string.Empty);
	}
}
