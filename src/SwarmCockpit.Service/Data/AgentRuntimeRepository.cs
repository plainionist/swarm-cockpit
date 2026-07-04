using SwarmCockpit.Contracts;

namespace SwarmCockpit.Service;

/// <summary>
/// In-memory runtime store for agent screens, operator inputs and logs.
/// Everything captured here is transient (screens refresh roughly every second
/// and the input queue drains within seconds), and only this service ever reads
/// or writes it, so there is no need to persist it to a database. State lives for
/// the lifetime of the service process and is rebuilt from the poller after a
/// restart.
/// </summary>
public sealed class AgentRuntimeRepository
{
    private const int MaxLogsPerAgent = 1000;

    private readonly object _gate = new();

    private readonly Dictionary<string, List<AgentLogLineViewModel>> _logsByAgent =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ScreenEntry> _screensByAgent =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<InputEntry> _inputs = new();

    private long _nextLogId;
    private long _nextInputId;

    public Task<AgentLogLineViewModel> AppendLogAsync(
        string agentName,
        string message,
        string stream,
        CancellationToken cancellationToken)
    {
        var trimmedName = agentName.Trim();
        var normalizedStream = string.IsNullOrWhiteSpace(stream) ? "stdout" : stream.Trim();
        var createdAt = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            var line = new AgentLogLineViewModel(
                ++_nextLogId,
                trimmedName,
                message,
                normalizedStream,
                createdAt);

            if (!_logsByAgent.TryGetValue(trimmedName, out var lines))
            {
                lines = new List<AgentLogLineViewModel>();
                _logsByAgent[trimmedName] = lines;
            }

            lines.Add(line);
            if (lines.Count > MaxLogsPerAgent)
            {
                lines.RemoveRange(0, lines.Count - MaxLogsPerAgent);
            }

            return Task.FromResult(line);
        }
    }

    public Task<IReadOnlyList<AgentLogLineViewModel>> GetRecentLogsAsync(string agentName, int take, CancellationToken cancellationToken)
    {
        var normalizedTake = Math.Clamp(take, 1, 500);

        lock (_gate)
        {
            if (!_logsByAgent.TryGetValue(agentName, out var lines) || lines.Count == 0)
            {
                return Task.FromResult<IReadOnlyList<AgentLogLineViewModel>>(Array.Empty<AgentLogLineViewModel>());
            }

            var start = Math.Max(0, lines.Count - normalizedTake);
            IReadOnlyList<AgentLogLineViewModel> rows = lines.GetRange(start, lines.Count - start);
            return Task.FromResult(rows);
        }
    }

    public Task<IReadOnlyList<AgentLogLineViewModel>> GetLogsAfterAsync(
        string agentName,
        long afterId,
        int take,
        CancellationToken cancellationToken)
    {
        var normalizedTake = Math.Clamp(take, 1, 500);

        lock (_gate)
        {
            if (!_logsByAgent.TryGetValue(agentName, out var lines) || lines.Count == 0)
            {
                return Task.FromResult<IReadOnlyList<AgentLogLineViewModel>>(Array.Empty<AgentLogLineViewModel>());
            }

            IReadOnlyList<AgentLogLineViewModel> rows = lines
                .Where(l => l.Id > afterId)
                .Take(normalizedTake)
                .ToList();
            return Task.FromResult(rows);
        }
    }

    public Task<AgentScreenViewModel> UpsertScreenAsync(
        string agentName,
        string content,
        CancellationToken cancellationToken)
    {
        var trimmedName = agentName.Trim();
        var capturedAt = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            // changed_at only advances on real content changes, so it reflects
            // terminal activity rather than the fixed capture cadence.
            var changedAt = capturedAt;
            if (_screensByAgent.TryGetValue(trimmedName, out var previous))
            {
                changedAt = string.Equals(previous.Content, content, StringComparison.Ordinal)
                    ? previous.ChangedAt
                    : capturedAt;
            }

            _screensByAgent[trimmedName] = new ScreenEntry(content, capturedAt, changedAt);

            return Task.FromResult(new AgentScreenViewModel(trimmedName, content, capturedAt));
        }
    }

    public Task<AgentScreenViewModel?> GetScreenAsync(string agentName, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_screensByAgent.TryGetValue(agentName, out var entry))
            {
                return Task.FromResult<AgentScreenViewModel?>(null);
            }

            return Task.FromResult<AgentScreenViewModel?>(
                new AgentScreenViewModel(agentName, entry.Content, entry.CapturedAt));
        }
    }

    public Task<AgentInputViewModel> EnqueueInputAsync(
        string agentName,
        string text,
        bool submit,
        CancellationToken cancellationToken)
    {
        var trimmedName = agentName.Trim();
        // Answers are single-line; strip embedded newlines so send-keys stays predictable.
        var sanitized = text.Replace("\r", string.Empty).Replace("\n", string.Empty);
        var createdAt = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            var entry = new InputEntry(++_nextInputId, trimmedName, sanitized, submit, createdAt);
            _inputs.Add(entry);
            return Task.FromResult(new AgentInputViewModel(entry.Id, trimmedName, sanitized, submit, createdAt));
        }
    }

    public Task<IReadOnlyList<AgentInputViewModel>> GetPendingInputsAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<AgentInputViewModel> rows = _inputs
                .OrderBy(i => i.Id)
                .Select(i => new AgentInputViewModel(i.Id, i.AgentName, i.Text, i.Submit, i.CreatedAt))
                .ToList();
            return Task.FromResult(rows);
        }
    }

    public Task<bool> MarkInputDeliveredAsync(long id, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var index = _inputs.FindIndex(i => i.Id == id);
            if (index < 0)
            {
                return Task.FromResult(false);
            }

            _inputs.RemoveAt(index);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<AgentActivitySnapshot>> GetLatestAgentActivityAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var latestByAgent = new Dictionary<string, AgentActivitySnapshot>(StringComparer.OrdinalIgnoreCase);

            foreach (var (agentName, lines) in _logsByAgent)
            {
                if (lines.Count == 0)
                {
                    continue;
                }

                var last = lines[^1];
                Merge(latestByAgent, new AgentActivitySnapshot(agentName, last.Message, last.CreatedAt));
            }

            foreach (var (agentName, entry) in _screensByAgent)
            {
                Merge(latestByAgent, new AgentActivitySnapshot(agentName, string.Empty, entry.ChangedAt));
            }

            IReadOnlyList<AgentActivitySnapshot> result = latestByAgent.Values
                .OrderBy(s => s.AgentName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<int> ClearLogsAsync(string agentName, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_logsByAgent.TryGetValue(agentName, out var lines))
            {
                var removed = lines.Count;
                _logsByAgent.Remove(agentName);
                return Task.FromResult(removed);
            }

            return Task.FromResult(0);
        }
    }

    private static void Merge(Dictionary<string, AgentActivitySnapshot> latestByAgent, AgentActivitySnapshot snapshot)
    {
        if (!latestByAgent.TryGetValue(snapshot.AgentName, out var existing)
            || snapshot.LastActivity > existing.LastActivity)
        {
            latestByAgent[snapshot.AgentName] = snapshot;
        }
    }

    private sealed record ScreenEntry(string Content, DateTimeOffset CapturedAt, DateTimeOffset ChangedAt);

    private sealed record InputEntry(long Id, string AgentName, string Text, bool Submit, DateTimeOffset CreatedAt);
}

public sealed record AgentActivitySnapshot(string AgentName, string LastMessage, DateTimeOffset LastActivity);
