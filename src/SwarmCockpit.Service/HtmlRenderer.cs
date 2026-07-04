using System.Net;
using System.Text;
using SwarmCockpit.Contracts;

namespace SwarmCockpit.Service;

internal static class HtmlRenderer
{
    public static string RenderDashboard(
        IReadOnlyList<QuestionViewModel> questions,
        IReadOnlyList<AgentStatusViewModel> statuses,
        IReadOnlyDictionary<string, IReadOnlyList<AgentLogLineViewModel>> logsByAgent,
        IReadOnlyDictionary<string, AgentScreenViewModel> screensByAgent)
    {
        var openQuestions = questions.Where(q => q.Status.Equals("open", StringComparison.OrdinalIgnoreCase)).ToList();
        var answeredQuestions = questions.Where(q => q.Status.Equals("answered", StringComparison.OrdinalIgnoreCase)).Take(20).ToList();

        var html = new StringBuilder();
        html.AppendLine("<!doctype html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head>");
        html.AppendLine("  <meta charset=\"utf-8\" />");
        html.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        html.AppendLine("  <title>Swarm Cockpit</title>");
        html.AppendLine("  <style>");
        html.AppendLine("    :root { --bg:#f4f6f8; --panel:#ffffff; --ink:#13293d; --muted:#526476; --accent:#0f7b6c; --line:#d9e2ea; }");
        html.AppendLine("    body { margin:0; font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background:linear-gradient(180deg,#edf2f7,#f8fafc); color:var(--ink); }");
        html.AppendLine("    main { width:100%; max-width:none; margin:0; padding:20px; box-sizing:border-box; }");
        html.AppendLine("    h1 { margin:0 0 8px; font-size:1.8rem; }");
        html.AppendLine("    .section { background:var(--panel); border:1px solid var(--line); border-radius:12px; padding:14px; margin-bottom:14px; }");
        html.AppendLine("    .item { border:1px solid var(--line); border-radius:10px; padding:12px; margin-bottom:10px; background:#fff; }");
        html.AppendLine("    .tabs { display:flex; gap:8px; border-bottom:1px solid var(--line); padding-bottom:8px; overflow:auto; }");
        html.AppendLine("    .tab { border:1px solid var(--line); background:#fff; color:var(--ink); border-radius:8px; padding:8px 10px; cursor:pointer; white-space:nowrap; }");
        html.AppendLine("    .tab.active { border-color:#0f7b6c; box-shadow:0 0 0 2px rgba(15,123,108,.15) inset; font-weight:700; }");
        html.AppendLine("    .tab-dot { display:inline-block; width:8px; height:8px; border-radius:50%; margin-right:6px; background:#9ca8b3; vertical-align:middle; }");
        html.AppendLine("    .tab-dot.running { background:#0f7b6c; }");
        html.AppendLine("    .tab-dot.blocked { background:#bf3b2a; }");
        html.AppendLine("    .tab-panel { display:none; margin-top:12px; }");
        html.AppendLine("    .tab-panel.active { display:block; }");
        html.AppendLine("    .tab-header { display:flex; justify-content:space-between; align-items:center; margin-bottom:8px; }");
        html.AppendLine("    .tab-actions { display:flex; gap:8px; align-items:center; }");
        html.AppendLine("    .tab-title { font-size:1.1rem; font-weight:700; }");
        html.AppendLine("    .tab-state { color:var(--muted); font-size:.9rem; }");
        html.AppendLine("    .meta { color:var(--muted); font-size:.9rem; }");
        html.AppendLine("    ul { margin:8px 0 8px 22px; }");
        html.AppendLine("    pre.log-full { white-space:pre; overflow:auto; background:#0b1220; color:#e6edf3; border:1px solid #1f2a3a; border-radius:8px; padding:12px; margin:0; width:100%; box-sizing:border-box; max-height:calc(100vh - 150px); font-family:'Cascadia Mono','Consolas','SFMono-Regular',ui-monospace,Menlo,monospace; font-size:12.5px; line-height:1.3; tab-size:4; }");
        html.AppendLine("    textarea { width:100%; min-height:70px; border:1px solid var(--line); border-radius:8px; padding:8px; font:inherit; }");
        html.AppendLine("    button { background:var(--accent); color:#fff; border:none; border-radius:8px; padding:8px 12px; cursor:pointer; }");
        html.AppendLine("    .btn-clear { background:#fff; color:var(--ink); border:1px solid var(--line); }");
        html.AppendLine("    .input-bar { display:flex; gap:8px; margin-top:10px; }");
        html.AppendLine("    .input-box { flex:1; border:1px solid var(--line); border-radius:8px; padding:9px 10px; font:inherit; font-family:'Cascadia Mono','Consolas',ui-monospace,monospace; }");
        html.AppendLine("    .input-box:disabled { background:#f1f4f7; color:var(--muted); }");
        html.AppendLine("    .btn-send { white-space:nowrap; }");
        html.AppendLine("    @media (max-width: 700px) { main { padding:12px; } h1 { font-size:1.4rem; } }");
        html.AppendLine("  </style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine("<main>");
        html.AppendLine("  <h1>Swarm Cockpit</h1>");

        html.AppendLine("  <section class=\"section\">");
        html.AppendLine("    <div class=\"tabs\" id=\"agent-tabs\">");
        for (var i = 0; i < statuses.Count; i++)
        {
            var status = statuses[i];
            var active = i == 0 ? " active" : string.Empty;
            html.AppendLine($"      <button type=\"button\" class=\"tab{active}\" data-agent=\"{Html(status.AgentName)}\" data-status=\"{Html(status.Status)}\">");
            html.AppendLine($"        <span class=\"tab-dot {Html(status.Status)}\"></span>{Html(status.AgentName)}");
            html.AppendLine("      </button>");
        }
        html.AppendLine("    </div>");

        html.AppendLine("    <div id=\"agent-panels\">");
        for (var i = 0; i < statuses.Count; i++)
        {
            var status = statuses[i];
            logsByAgent.TryGetValue(status.AgentName, out var agentLogs);
            string logText;
            if (screensByAgent.TryGetValue(status.AgentName, out var screen) && !string.IsNullOrWhiteSpace(screen.Content))
            {
                logText = screen.Content;
            }
            else if (agentLogs is not null && agentLogs.Count > 0)
            {
                logText = string.Join("\n", agentLogs.Select(log => $"[{log.CreatedAt:HH:mm:ss}] {log.Message}"));
            }
            else
            {
                logText = "No output yet.";
            }

            var panelActive = i == 0 ? " active" : string.Empty;
            html.AppendLine($"      <article class=\"tab-panel{panelActive}\" data-agent=\"{Html(status.AgentName)}\">");
            html.AppendLine("        <div class=\"tab-header\">");
            html.AppendLine($"          <div class=\"tab-title\">{Html(status.AgentName)}</div>");
            html.AppendLine("          <div class=\"tab-actions\">");
            html.AppendLine("            <div class=\"tab-state\" data-role=\"state\"></div>");
            html.AppendLine("            <button type=\"button\" class=\"btn-clear\" data-role=\"clear\">Clear logs</button>");
            html.AppendLine("          </div>");
            html.AppendLine("        </div>");
            html.AppendLine($"        <pre class=\"log-full\" data-role=\"log\">{Html(logText)}</pre>");
            html.AppendLine("        <form class=\"input-bar\" data-role=\"input-form\">");
            html.AppendLine($"          <input type=\"text\" class=\"input-box\" data-role=\"input\" placeholder=\"Type a reply and press Enter to send to {Html(status.AgentName)}'s terminal\" autocomplete=\"off\" />");
            html.AppendLine("          <button type=\"submit\" class=\"btn-send\">Send</button>");
            html.AppendLine("        </form>");
            html.AppendLine("      </article>");
        }
        html.AppendLine("    </div>");
        html.AppendLine("  </section>");

        html.AppendLine("  <section class=\"section\">");
        html.AppendLine($"    <h2>Open ({openQuestions.Count})</h2>");
        if (openQuestions.Count == 0)
        {
            html.AppendLine("    <p class=\"meta\">No open questions.</p>");
        }
        else
        {
            foreach (var question in openQuestions)
            {
                html.AppendLine("    <article class=\"item\">");
                html.AppendLine($"      <div><strong>{Html(question.AskingAgent)}</strong> <span class=\"meta\">asked at {question.CreatedAt:O}</span></div>");
                html.AppendLine($"      <div><strong>Context:</strong> {Html(question.Context)}</div>");
                html.AppendLine($"      <div><strong>Question:</strong> {Html(question.Question)}</div>");
                html.AppendLine($"      <div><strong>Recommendation:</strong> {Html(question.Recommendation)}</div>");
                if (question.Options.Count > 0)
                {
                    html.AppendLine("      <div><strong>Options:</strong></div>");
                    html.AppendLine("      <ul>");
                    foreach (var option in question.Options)
                    {
                        html.AppendLine($"        <li>{Html(option)}</li>");
                    }
                    html.AppendLine("      </ul>");
                }

                html.AppendLine($"      <form method=\"post\" action=\"/questions/{Html(question.Id)}/answer\">");
                html.AppendLine("        <label for=\"answer\">Answer</label>");
                html.AppendLine("        <textarea id=\"answer\" name=\"answer\" required></textarea>");
                html.AppendLine("        <div style=\"margin-top:8px\"><button type=\"submit\">Submit answer</button></div>");
                html.AppendLine("      </form>");
                html.AppendLine("    </article>");
            }
        }

        html.AppendLine("  </section>");

        html.AppendLine("  <section class=\"section\">");
        html.AppendLine($"    <h2>Recently Answered ({answeredQuestions.Count})</h2>");
        if (answeredQuestions.Count == 0)
        {
            html.AppendLine("    <p class=\"meta\">No answered questions yet.</p>");
        }
        else
        {
            foreach (var question in answeredQuestions)
            {
                html.AppendLine("    <article class=\"item\">");
                html.AppendLine($"      <div><strong>{Html(question.AskingAgent)}</strong> <span class=\"meta\">answered at {question.AnsweredAt:O}</span></div>");
                html.AppendLine($"      <div><strong>Question:</strong> {Html(question.Question)}</div>");
                html.AppendLine($"      <div><strong>Answer:</strong> {Html(question.Answer ?? string.Empty)}</div>");
                html.AppendLine("    </article>");
            }
        }

        html.AppendLine("  </section>");
        html.AppendLine("  <script>");
        html.AppendLine("    (function () {");
        html.AppendLine("      const tabs = Array.from(document.querySelectorAll('.tab')); ");
        html.AppendLine("      const panels = Array.from(document.querySelectorAll('.tab-panel')); ");
        html.AppendLine("      let activeAgent = tabs.length ? tabs[0].dataset.agent : null;");
        html.AppendLine("      function byAgent(items, agent) { return items.find(item => item.dataset.agent === agent); }");
        html.AppendLine("      const MONO = \"'Cascadia Mono','Consolas','SFMono-Regular',ui-monospace,Menlo,monospace\";");
        html.AppendLine("      let charRatio = null;");
        html.AppendLine("      function charWidthRatio() {");
        html.AppendLine("        if (charRatio) return charRatio;");
        html.AppendLine("        const ruler = document.createElement('span');");
        html.AppendLine("        ruler.style.cssText = 'position:absolute;visibility:hidden;white-space:pre;font-size:100px;font-family:' + MONO + ';';");
        html.AppendLine("        ruler.textContent = 'MMMMMMMMMMMMMMMMMMMM';");
        html.AppendLine("        document.body.appendChild(ruler);");
        html.AppendLine("        charRatio = (ruler.getBoundingClientRect().width / 20) / 100;");
        html.AppendLine("        ruler.remove();");
        html.AppendLine("        return charRatio || 0.6;");
        html.AppendLine("      }");
        html.AppendLine("      function fitFont(node) {");
        html.AppendLine("        const text = node.textContent || '';");
        html.AppendLine("        const lines = text.split('\\n');");
        html.AppendLine("        let cols = 1;");
        html.AppendLine("        for (const line of lines) { if (line.length > cols) cols = line.length; }");
        html.AppendLine("        const avail = node.clientWidth - 24;");
        html.AppendLine("        if (avail <= 0) return;");
        html.AppendLine("        let fs = avail / (cols * charWidthRatio());");
        html.AppendLine("        fs = Math.max(6, Math.min(20, fs));");
        html.AppendLine("        node.style.fontSize = fs.toFixed(2) + 'px';");
        html.AppendLine("      }");
        html.AppendLine("      function setActive(agent) {");
        html.AppendLine("        activeAgent = agent;");
        html.AppendLine("        tabs.forEach(t => t.classList.toggle('active', t.dataset.agent === agent));");
        html.AppendLine("        panels.forEach(p => p.classList.toggle('active', p.dataset.agent === agent));");
        html.AppendLine("      }");
        html.AppendLine("      async function refreshStatuses() {");
        html.AppendLine("        const response = await fetch('/api/agents/status', { cache: 'no-store' });");
        html.AppendLine("        if (!response.ok) return;");
        html.AppendLine("        const data = await response.json();");
        html.AppendLine("        const states = (data && data.agents) ? data.agents : []; ");
        html.AppendLine("        states.forEach(state => {");
        html.AppendLine("          const tab = byAgent(tabs, state.agentName);");
        html.AppendLine("          if (!tab) return;");
        html.AppendLine("          tab.dataset.status = state.status || 'idle';");
        html.AppendLine("          const dot = tab.querySelector('.tab-dot');");
        html.AppendLine("          if (dot) { dot.className = 'tab-dot ' + (state.status || 'idle'); }");
        html.AppendLine("          const panel = byAgent(panels, state.agentName);");
        html.AppendLine("          if (!panel) return;");
        html.AppendLine("          const stateText = panel.querySelector(\"[data-role='state']\");");
        html.AppendLine("          if (stateText) { stateText.textContent = state.needsHumanInput ? 'Needs input' : ''; }");
        html.AppendLine("        });");
        html.AppendLine("      }");
        html.AppendLine("      async function refreshActiveLog() {");
        html.AppendLine("        if (!activeAgent) return;");
        html.AppendLine("        const panel = byAgent(panels, activeAgent);");
        html.AppendLine("        if (!panel) return;");
        html.AppendLine("        const logNode = panel.querySelector(\"[data-role='log']\");");
        html.AppendLine("        if (!logNode) return;");
        html.AppendLine("        const nearBottom = (logNode.scrollHeight - logNode.scrollTop - logNode.clientHeight) < 24;");
        html.AppendLine("        const agent = activeAgent;");
        html.AppendLine("        const screenRes = await fetch('/api/agents/' + encodeURIComponent(agent) + '/screen', { cache: 'no-store' });");
        html.AppendLine("        if (agent !== activeAgent) return;");
        html.AppendLine("        if (screenRes.ok) {");
        html.AppendLine("          const screen = await screenRes.json();");
        html.AppendLine("          if (screen && typeof screen.content === 'string' && screen.content.trim().length) {");
        html.AppendLine("            if (logNode.textContent !== screen.content) { logNode.textContent = screen.content; }");
        html.AppendLine("            fitFont(logNode);");
        html.AppendLine("            if (nearBottom) { logNode.scrollTop = logNode.scrollHeight; }");
        html.AppendLine("            return;");
        html.AppendLine("          }");
        html.AppendLine("        }");
        html.AppendLine("        const response = await fetch('/api/agents/' + encodeURIComponent(agent) + '/logs?take=600', { cache: 'no-store' });");
        html.AppendLine("        if (agent !== activeAgent || !response.ok) return;");
        html.AppendLine("        const lines = await response.json();");
        html.AppendLine("        const text = (lines || []).map(line => '[' + new Date(line.createdAt).toLocaleTimeString('en-GB', { hour12: false }) + '] ' + line.message).join('\\n');");
        html.AppendLine("        logNode.textContent = text || 'No output yet.';");
        html.AppendLine("        if (nearBottom) { logNode.scrollTop = logNode.scrollHeight; }");
        html.AppendLine("      }");
        html.AppendLine("      async function clearActiveLog() {");
        html.AppendLine("        if (!activeAgent) return;");
        html.AppendLine("        await fetch('/api/agents/' + encodeURIComponent(activeAgent) + '/logs', { method: 'DELETE' });");
        html.AppendLine("        await refreshActiveLog();");
        html.AppendLine("      }");
        html.AppendLine("      tabs.forEach(tab => tab.addEventListener('click', () => { setActive(tab.dataset.agent); refreshActiveLog().catch(() => {}); }));");
        html.AppendLine("      panels.forEach(panel => {");
        html.AppendLine("        const clearBtn = panel.querySelector(\"[data-role='clear']\");");
        html.AppendLine("        if (clearBtn) { clearBtn.addEventListener('click', () => clearActiveLog().catch(() => {})); }");
        html.AppendLine("        const form = panel.querySelector(\"[data-role='input-form']\");");
        html.AppendLine("        const box = panel.querySelector(\"[data-role='input']\");");
        html.AppendLine("        if (form && box) {");
        html.AppendLine("          form.addEventListener('submit', async (ev) => {");
        html.AppendLine("            ev.preventDefault();");
        html.AppendLine("            const agent = panel.dataset.agent;");
        html.AppendLine("            const text = box.value;");
        html.AppendLine("            if (!agent) return;");
        html.AppendLine("            box.disabled = true;");
        html.AppendLine("            try {");
        html.AppendLine("              const res = await fetch('/api/agents/' + encodeURIComponent(agent) + '/input', {");
        html.AppendLine("                method: 'POST', headers: { 'Content-Type': 'application/json' },");
        html.AppendLine("                body: JSON.stringify({ text: text, submit: true })");
        html.AppendLine("              });");
        html.AppendLine("              if (res.ok) { box.value = ''; }");
        html.AppendLine("            } catch (_) { }");
        html.AppendLine("            box.disabled = false;");
        html.AppendLine("            box.focus();");
        html.AppendLine("          });");
        html.AppendLine("        }");
        html.AppendLine("      });");
        html.AppendLine("      setActive(activeAgent);");
        html.AppendLine("      let resizeTimer = null;");
        html.AppendLine("      window.addEventListener('resize', () => {");
        html.AppendLine("        clearTimeout(resizeTimer);");
        html.AppendLine("        resizeTimer = setTimeout(() => {");
        html.AppendLine("          const panel = byAgent(panels, activeAgent);");
        html.AppendLine("          if (!panel) return;");
        html.AppendLine("          const logNode = panel.querySelector(\"[data-role='log']\");");
        html.AppendLine("          if (logNode) fitFont(logNode);");
        html.AppendLine("        }, 120);");
        html.AppendLine("      });");
        html.AppendLine("      const refreshAll = async () => { try { await refreshStatuses(); await refreshActiveLog(); } catch (_) { } }; ");
        html.AppendLine("      refreshAll();");
        html.AppendLine("      setInterval(refreshAll, 2000);");
        html.AppendLine("    })();");
        html.AppendLine("  </script>");
        html.AppendLine("</main>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");

        return html.ToString();
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);
}
