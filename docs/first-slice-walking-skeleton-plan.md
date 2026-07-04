# First Slice Walking Skeleton Plan

## Goal

Deliver the smallest useful Swarm Remote Control experience that proves the core loop:

```text
Agent asks a structured question
Operator sees it in a browser
Operator answers it
Agent receives the answer and continues
```

This slice should be useful even without SwarmForge integration. It should let an operator run a local sidecar service, open a lightweight web UI, submit a test question from a command line script, answer it from another device on the same trusted network, and see the waiting command return the answer.

The slice must not modify an existing SwarmForge checkout or replace any SwarmForge scripts.

## Walking Skeleton Value

The first slice provides value when the operator can say:

```text
I can start the cockpit beside SwarmForge, simulate a blocked agent question, answer it from the UI, and watch the blocked command continue.
```

That is intentionally narrow, but it exercises the most important product behavior end to end:

- a local control center process runs outside SwarmForge,
- an agent-facing script can create a structured blocking question,
- the question is visible in a web UI,
- the operator can answer it,
- the waiting script receives the answer,
- no SwarmForge files are changed.

## Scope

### Included

- Local sidecar web service bound to localhost by default.
- Minimal browser UI with:
  - open questions,
  - answered questions,
  - question detail,
  - answer form.
- Agent-facing command line script:
  - creates a structured question,
  - waits for an answer,
  - prints the answer to stdout,
  - exits non-zero only for local script or service errors.
- Simple local persistence so questions survive a service restart during manual review.
- Installation instructions that install Swarm Remote Control beside SwarmForge instead of into SwarmForge.
- Manual review script or checklist for demonstrating the slice.

### Excluded

- Wrapping SwarmForge lifecycle scripts.
- Modifying SwarmForge workflow, constitution, or handoff files.
- Remote approvals.
- Push notifications.
- Pause, resume, stop, or other session commands.
- Authentication beyond local/trusted-network assumptions.
- Full agent status reconstruction.
- Public internet deployment.

## Proposed Components

```text
swarm-cockpit
  service
    owns HTTP API, persistence, and web UI serving
  scripts
    swarm-ask waits for an operator answer
  data
    local development database or JSON store
```

The exact implementation stack can be chosen during implementation, but the architecture should preserve these boundaries:

- The service owns question state.
- The script owns the agent-facing blocking behavior.
- The UI owns operator review and answer entry.
- SwarmForge remains untouched.

## Minimal Data Model

### Question

Fields required for the first slice:

- `id`
- `askingAgent`
- `context`
- `question`
- `options`
- `recommendation`
- `status`: `open` or `answered`
- `answer`
- `createdAt`
- `answeredAt`

The data model should be compatible with the larger specification, but only these fields are required for the first slice.

## Minimal API

The API should be intentionally small:

- `POST /api/questions`
  - creates a question,
  - returns the question id.
- `GET /api/questions`
  - lists open and recently answered questions.
- `GET /api/questions/{id}`
  - returns one question.
- `POST /api/questions/{id}/answer`
  - stores the operator answer.
- `GET /api/questions/{id}/answer`
  - used by the waiting script to poll until answered.

Polling is acceptable for the walking skeleton. Server-sent events or websockets can come later if needed.

## Agent Script Behavior

The first agent-facing script should be explicit and testable from a terminal.

Example shape:

```text
swarm-ask \
  --agent Implementer \
  --context "Need product decision for first slice" \
  --question "Should the first slice store questions in a JSON file?" \
  --option "Yes, keep it simple" \
  --option "No, use SQLite now" \
  --recommendation "Yes, keep it simple"
```

Expected behavior:

1. The script submits the question to the local service.
2. The script prints the question id and UI URL to stderr so a human can find it during review.
3. The script waits until the question is answered.
4. The script prints the answer to stdout.
5. If the service is unavailable, the script fails clearly without changing any SwarmForge state.

This script is allowed to block. Blocking is the purpose of the agent-to-human question workflow.

## UI Behavior

The first UI should be simple but usable on desktop and mobile.

The default screen should show:

- open questions first,
- asking agent,
- context,
- question text,
- recommendation,
- answer controls,
- recently answered questions below.

The UI does not need marketing content or dashboards yet. It should go straight to the operator task.

## Installation Strategy

The first slice must install as a sidecar:

```text
Existing SwarmForge checkout        unchanged
Swarm Remote Control checkout       separate directory
Operator PATH                       may include swarm-cockpit scripts
Swarm Remote Control service        separate local process
```

The install process must not:

- copy files into SwarmForge,
- rename SwarmForge scripts,
- replace SwarmForge scripts,
- edit SwarmForge configuration,
- require a SwarmForge reinstall.

Any future wrapper installation must be opt-in and reversible. It is outside this first slice.

## Manual Review Scenario

Reviewer steps:

1. Install and start Swarm Remote Control using the README instructions.
2. Open the UI in a browser.
3. In a terminal, run the sample `swarm-ask` command.
4. Verify the question appears in the UI.
5. Answer the question in the UI.
6. Verify the terminal command prints the answer and exits successfully.
7. Stop the service.
8. Verify no files in the SwarmForge checkout were changed.

## Acceptance Criteria

- A fresh checkout can start the sidecar service using documented commands.
- The service binds to localhost by default.
- A sample `swarm-ask` command creates a visible open question.
- The UI can answer the question from a browser.
- The waiting command prints the answer and exits successfully.
- Question state survives a service restart during manual review.
- The README explains how to install and run the sidecar without modifying SwarmForge.
- The implementation does not require editing an existing SwarmForge checkout.

## Risks and Follow-Up Decisions

- Persistence choice can stay simple for the walking skeleton, but the implementation should avoid baking UI behavior into storage details.
- Authentication is deferred, so the service should bind to localhost by default and document trusted-network exposure separately.
- Polling is enough for the first slice, but long-polling or server-sent events may be needed once mobile notifications and richer status updates arrive.
- Wrapper-based telemetry should wait until the sidecar question loop is proven.

## Next Slice Candidates

After this walking skeleton works, the next slice should be one of:

- add a non-blocking `swarm-notify` script and event timeline,
- add explicit approval requests as a separate object from questions,
- add one opt-in transparent wrapper around a low-risk SwarmForge lifecycle script,
- add mobile-friendly notification hooks for open questions.