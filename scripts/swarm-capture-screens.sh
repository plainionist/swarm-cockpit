#!/usr/bin/env bash
set -euo pipefail

# Periodically mirror the *rendered* visible screen of each agent's tmux pane to
# the cockpit. Unlike pipe-pane (which streams raw TUI redraw bytes and produces
# shredded fragments), `tmux capture-pane -p` returns what the user actually sees.

socket=""
base_url="http://localhost:5959"
interval=1
declare -a panes=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --socket)
      socket="$2"
      shift 2
      ;;
    --base-url)
      base_url="$2"
      shift 2
      ;;
    --interval)
      interval="$2"
      shift 2
      ;;
    --pane)
      # Format: agent=target  (e.g. Implementer=swarm:0.0)
      panes+=("$2")
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

if [[ -z "${socket}" ]]; then
  echo "--socket is required" >&2
  exit 1
fi

if [[ ${#panes[@]} -eq 0 ]]; then
  echo "At least one --pane <agent>=<target> is required" >&2
  exit 1
fi

log() {
  echo "[$(date '+%H:%M:%S')] $*" >&2
}

log "capture poller starting: socket=${socket} base_url=${base_url} interval=${interval}s panes=${panes[*]}"
log "tmux version: $(tmux -V 2>&1 || echo unknown)"

# capture-pane flags: -p prints to stdout. -J joins wrapped lines but is not
# supported on very old tmux, so detect support once and fall back to plain -p.
first_target="${panes[0]#*=}"
capture_flags=(-p -J)
if ! tmux -S "${socket}" capture-pane -p -J -t "${first_target}" >/dev/null 2>&1; then
  capture_flags=(-p)
  log "tmux capture-pane -J not supported; falling back to plain -p"
fi
log "using capture flags: ${capture_flags[*]}"

first_pass=1

while true; do
  for entry in "${panes[@]}"; do
    agent="${entry%%=*}"
    target="${entry#*=}"

    if [[ -z "${agent}" || -z "${target}" ]]; then
      continue
    fi

    if ! content="$(tmux -S "${socket}" capture-pane "${capture_flags[@]}" -t "${target}" 2>&1)"; then
      log "capture-pane FAILED for ${agent} (${target}): ${content}"
      continue
    fi

    if [[ "${first_pass}" -eq 1 ]]; then
      lines="$(printf '%s' "${content}" | wc -l)"
      log "captured ${agent} (${target}): ${lines} lines"
    fi

    # Send the rendered screen verbatim as text/plain; no JSON escaping needed.
    # Pipe via stdin so a leading '@' in the screen is not treated as a file ref.
    if ! printf '%s' "${content}" | curl -fsS -X PUT "${base_url}/api/agents/${agent}/screen" \
      -H "Content-Type: text/plain; charset=utf-8" \
      --data-binary @- >/dev/null 2>&1; then
      log "PUT screen FAILED for ${agent} -> ${base_url}"
    fi
  done

  first_pass=0
  sleep "${interval}"
done
