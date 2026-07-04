# Swarm Remote Control — High-Level Specification

## Purpose

Swarm Remote Control is a companion control plane for (SwarmForge)[https://github.com/unclebob/swarm-forge].

Its goal is to let the human operator monitor, guide, and unblock a running SwarmForge agent team without sitting in front of the terminal.

The terminal should remain the execution environment.
Swarm Remote Control becomes the human interaction surface.

The system should make it possible to:

- see what each agent is doing,
- notice when an agent is blocked,
- answer clarification questions remotely,
- approve or reject important decisions,
- receive completion or failure notifications,
- pause, resume, or stop the swarm when needed,
- keep SwarmForge upgradeable and untouched as much as possible.

The product should feel less like a remote desktop and more like a mission control board for an agent team.

---

## Core Idea

SwarmForge remains the execution engine.
Swarm Remote Control is an add-on around it.

```text
SwarmForge
  owns agent execution, tmux sessions, worktrees, handoffs, and workflow rules

Swarm Remote Control
  owns human interaction, status visibility, remote answers, approvals, and notifications

Wrapper / adapter layer
  observes SwarmForge lifecycle events and connects agents to the Remote Control Center
```

The architecture should avoid turning Swarm Remote Control into a fork of SwarmForge.
SwarmForge should continue to work even if Swarm Remote Control is unavailable.

---

## Architectural Principles

### 1. SwarmForge stays the engine

Swarm Remote Control must not replace SwarmForge.

SwarmForge continues to manage:

- agent roles,
- tmux sessions,
- worktrees,
- handoff files,
- agent-to-agent coordination,
- existing workflow scripts,
- the main development loop.

Swarm Remote Control adds visibility and remote human interaction around this existing engine.

### 2. Add-on first, fork never

The preferred architecture is an external add-on.

Swarm Remote Control should be deployable beside SwarmForge without modifying SwarmForge source code wherever possible.

Acceptable integration mechanisms:

- wrapper scripts,
- additional helper scripts,
- local workflow/constitution instructions,
- read-only observation of SwarmForge state,
- launcher scripts,
- environment configuration.

Avoid coupling the Remote Control Center directly into SwarmForge internals.

### 3. Wrappers are allowed, but must be transparent

Some SwarmForge lifecycle events already pass through scripts.
Those scripts are natural integration points.

Wrapping them is useful because lifecycle events become reliable and do not depend on agents remembering to report status manually.

However, wrappers must be transparent by default.

A wrapper around an existing SwarmForge script must preserve:

- the same script name,
- the same arguments,
- the same stdout,
- the same stderr,
- the same exit code,
- the same behavior,
- the same failure semantics.

The wrapper may add best-effort telemetry as a side effect.

If the Remote Control Center is down, slow, unreachable, or misconfigured, SwarmForge must continue to work.
Telemetry failures must not break the swarm.

### 4. Separate agent-to-agent and agent-to-human communication

SwarmForge handoff scripts remain the agent-to-agent protocol.

Swarm Remote Control introduces a separate agent-to-human protocol.

```text
Agent-to-agent:
  use normal SwarmForge handoff workflow

Agent-to-human:
  use Swarm Remote Control ask / notify / approval workflow
```

These two protocols should not be mixed.

Agent-to-agent handoffs are for work coordination.
Agent-to-human questions are for clarification, approval, risk escalation, or operator decisions.

### 5. Console is no longer the main human interface

The console remains useful for execution and debugging.
It should not be the only place where the human can interact with agents.

When remote mode is active, agents should not rely on terminal-only questions.
They should route human decisions through Swarm Remote Control.

The human should be able to respond from:

- a browser,
- a phone,
- a tablet,
- another machine on the network.

### 6. Human interaction should be structured

The system should avoid vague chat-style interruptions when an agent is blocked.

A good remote question should include:

- the asking agent,
- the task or slice context,
- the concrete question,
- available options,
- the agent’s recommendation,
- the consequence of each option when relevant,
- whether the agent is blocked until answered.

The operator should be able to answer quickly, especially on mobile.

---

## Main Capabilities

### 1. Agent status board

The Remote Control Center should show the current state of the swarm.

At minimum, the operator should see:

- active sessions,
- participating agents,
- agent roles,
- current agent status,
- current task or phase,
- last activity time,
- whether an agent is blocked,
- whether human input is needed.

The status board should answer the question:

```text
What is my swarm doing right now?
```

### 2. Lifecycle event timeline

The system should collect and display important lifecycle events.

Examples:

- session started,
- agent became ready,
- handoff created,
- handoff accepted,
- task completed,
- agent became idle,
- script failed,
- build failed,
- tests passed,
- human input requested,
- human answer received.

The timeline should give the operator confidence that the swarm is progressing.

It should not require reading raw terminal logs for normal monitoring.

### 3. Blocking question queue

The most important feature is a queue of open human questions.

When an agent cannot safely continue, it should create a structured question.

The question stays open until the operator answers, cancels, or redirects it.

The agent that asked the question may block until the answer is available.

The queue should make blocked work visible immediately.

The operator should be able to answer from a mobile-friendly UI.

### 4. Notifications

The Remote Control Center should support notifications for important events.

Typical notification triggers:

- an agent needs input,
- a session completed,
- a session failed,
- tests failed,
- approval is requested,
- the swarm became idle,
- a long-running operation finished.

Notifications are not the source of truth.
The Remote Control Center is the source of truth.
Notifications only bring the operator back to the control center.

### 5. Remote answer flow

The answer flow should work like this:

```text
Agent asks structured question
        ↓
Remote Control Center stores it
        ↓
Operator gets notification / sees open question
        ↓
Operator answers in web or mobile UI
        ↓
Agent receives answer
        ↓
Agent continues
```

This is the central workflow.

### 6. Remote approvals

Some interactions are not clarification questions but approval gates.

Examples:

- approve implementation result,
- approve handoff result,
- approve moving to the next slice,
- approve applying a larger refactoring,
- approve stopping the current approach,
- approve creating follow-up tickets.

Approval requests should be explicit and structured.

The system should distinguish:

```text
question = agent needs information
approval = agent proposes an action and asks permission
notification = agent reports something, no answer required
```

### 7. Session commands

The operator should be able to send high-level commands to a running swarm session.

Examples:

- pause after current safe point,
- resume,
- stop,
- stop after current task,
- request status summary,
- request final summary,
- switch to remote/mobile mode,
- exit remote/mobile mode.

Commands should be high-level and safe.
The first version does not need a full remote shell.

### 8. Mobile mode

Mobile mode means all meaningful human interaction is routed through Swarm Remote Control.

When mobile mode is active:

- agents should not ask terminal-only questions,
- blocking questions go to the Remote Control Center,
- meaningful completions create notifications,
- the operator can answer from a phone,
- the swarm can continue without the operator watching the console.

Mobile mode should be explicit and reversible.

### 9. Read-only observation

Where possible, Swarm Remote Control should observe existing SwarmForge state without modifying it.

Useful state may include:

- session metadata,
- role metadata,
- handoff state,
- worktree state,
- recent lifecycle changes,
- process liveness.

Read-only observation is useful for status reconstruction and resilience.

### 10. Wrapper-based lifecycle telemetry

Read-only observation may not be enough for reliable lifecycle events.

For high-value lifecycle actions, wrapper scripts should emit telemetry events while preserving original behavior.

Candidate wrapped actions:

- creating a handoff,
- accepting the next task,
- completing the current task.

The wrapper layer exists to make the Remote Control Center reliable without depending on agents to manually report every important state change.

---

## User Experience Goals

### Desktop UI

The desktop UI should be useful when the operator wants overview and detail.

It should show:

- session overview,
- agent cards,
- event timeline,
- open questions,
- approval requests,
- recent completions,
- links to relevant worktree, diff, ticket, or log where available.

### Mobile UI

The mobile UI should focus on fast decisions.

The first screen should prioritize:

- blocked agents,
- open questions,
- approval requests,
- latest important events,
- emergency stop / pause.

The mobile UI should not require reading long logs.

Each question should be answerable with minimal typing when possible.

Examples:

- approve recommendation,
- choose option A/B/C,
- write short answer,
- ask for more detail,
- reject and explain.

### Operator confidence

The system should help the operator trust the swarm.

It should make visible:

- who is working,
- what changed,
- who is blocked,
- why human input is needed,
- what the agent recommends,
- what happened after the operator answered.

The operator should not need to remember terminal history or manually inspect multiple tmux panes just to know whether progress is happening.

---

## Integration Model

### Existing SwarmForge scripts

Existing SwarmForge workflow scripts remain the official workflow protocol.

Swarm Remote Control may wrap selected scripts for telemetry.

The wrapped script names should remain the same from the agent perspective.

The original implementation should remain callable behind the wrapper.

The wrapper must be reversible.

### New Remote Control scripts

Swarm Remote Control should provide its own scripts for human interaction.

Conceptually:

- notify operator,
- ask operator and wait for answer,
- request approval and wait for decision,
- report status,
- enter or exit mobile mode.

Agents can be instructed to use these scripts when they need human interaction.

### Local workflow instructions

SwarmForge workflow or constitution instructions may be extended locally to describe the remote-control behavior.

Those instructions should explain:

- when to ask the human,
- when to notify,
- when to request approval,
- how to behave in mobile mode,
- that normal SwarmForge handoffs remain unchanged,
- that remote-control scripts are for agent-to-human interaction only.

The instructions should be short and operational.
They should not duplicate the full product specification.

---

## Reliability Requirements

### Remote Control must not break SwarmForge

The most important reliability rule:

```text
If Swarm Remote Control fails, SwarmForge should still work.
```

Telemetry and notifications are best-effort.
They must not make the main SwarmForge workflow fragile.

### Blocking questions are allowed to block

A remote ask operation may intentionally block the asking agent until the operator answers.

That is different from telemetry.

Telemetry must not block the workflow.
Human questions may block because that is their purpose.

### Preserve original script behavior

Wrappers around existing SwarmForge scripts must preserve behavior exactly.

They should never modify the meaning of the wrapped operation.

### Avoid hidden destructive actions

Remote commands should be safe and explicit.

Dangerous actions should require confirmation or be designed as safe-point commands.

Examples:

- prefer “stop after current task” over immediate process kill,
- prefer “pause after safe point” over hard suspend,
- distinguish “cancel question” from “stop session”.

---

## Non-Goals for the First Version

The first version should not try to become everything.

Non-goals:

- full remote desktop,
- full web terminal,
- complete log streaming platform,
- replacement for tmux,
- replacement for SwarmForge handoffs,
- multi-user enterprise permission system,
- public internet exposure,
- complex orchestration engine,
- full project management system,
- complete GitHub/GHE replacement.

The first valuable version is a control plane, not a second IDE.

---

## Security and Access Expectations

Swarm Remote Control is powerful because it can influence running agents.
It should not be exposed publicly without protection.

Expected access models:

- local network,
- VPN,
- Tailscale or similar private network,
- authenticated internal deployment.

The system should assume that remote commands and answers can affect code changes.
Access should be limited to trusted operators.

Secrets, tokens, and credentials should not be exposed in the UI or stored unnecessarily.

---

## Conceptual Data Objects

The implementation may choose the actual storage model.
Conceptually, the system needs these objects.

### Session

Represents one running SwarmForge session.

Important information:

- session id,
- repository,
- branch or worktree context,
- start time,
- current status,
- mobile mode status.

### Agent

Represents one agent in a session.

Important information:

- agent name,
- role,
- current status,
- current task,
- last activity,
- blocked/unblocked state.

### Event

Represents something that happened.

Important information:

- timestamp,
- session,
- agent if applicable,
- event type,
- title,
- details,
- severity.

### Question

Represents a blocking human decision request.

Important information:

- asking agent,
- context,
- question,
- options,
- recommendation,
- status,
- answer,
- timestamps.

### Approval Request

Represents a proposed action waiting for operator approval.

Important information:

- requesting agent,
- proposed action,
- rationale,
- risk,
- recommendation,
- decision,
- timestamps.

### Command

Represents an operator command to a session or agent.

Important information:

- command type,
- target session or agent,
- payload,
- status,
- result.

---

## Expected Behavior Examples

### Example 1: Agent completes work

```text
Verifier completes acceptance test update.
Verifier calls completion workflow.
Wrapped SwarmForge script emits lifecycle event.
Remote Control Center shows Verifier as completed/idle.
Operator receives optional notification.
```

### Example 2: Agent needs clarification

```text
Implementer finds ambiguous behavior.
Implementer creates a structured question.
Remote Control Center stores question and notifies operator.
Operator answers from phone.
Implementer receives answer and continues.
```

### Example 3: Operator goes mobile

```text
Operator enables mobile mode.
Agents route human interaction through Remote Control.
Terminal remains active but is no longer required for decisions.
Operator receives notifications and answers from phone.
```

### Example 4: Swarm Remote Control is unavailable

```text
Agent calls normal SwarmForge handoff script.
Wrapper attempts telemetry.
Telemetry fails because Control Center is down.
Wrapper preserves original script result.
SwarmForge continues normally.
```

---

## Success Criteria

Swarm Remote Control is successful when:

- the operator can leave the machine and still unblock agents,
- blocked agents are visible immediately,
- important completions are visible without reading terminal logs,
- agents can ask structured questions and continue after remote answers,
- lifecycle events are captured reliably through wrappers,
- SwarmForge remains usable without the add-on,
- the integration stays reversible and upgrade-friendly,
- the system reduces the need to monitor tmux panes manually.

The first strong milestone is reached when the operator can say:

```text
I can leave the desk, get notified when the swarm needs me, answer from my phone, and the agents continue.
```

---

## Design Summary

Swarm Remote Control should be built as a sidecar control plane for SwarmForge.

It should combine:

- transparent wrappers for reliable lifecycle telemetry,
- new remote-control scripts for agent-to-human interaction,
- a web/mobile UI for questions, approvals, status, and commands,
- optional notifications to bring the operator back when needed,
- local workflow instructions telling agents when to use the remote-control channel.

The architecture should preserve the core boundary:

```text
SwarmForge runs the swarm.
Swarm Remote Control lets the human control and guide it remotely.
```
