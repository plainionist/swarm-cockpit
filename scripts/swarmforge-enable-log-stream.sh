#!/usr/bin/env bash
set -euo pipefail

base_url="http://localhost:5959"
base_url_explicit=0
config_file="swarmforge/swarmforge.conf"
socket_file=".swarmforge/tmux-socket"
sessions_file=".swarmforge/sessions.tsv"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --base-url)
      base_url="$2"
      base_url_explicit=1
      shift 2
      ;;
    --config)
      config_file="$2"
      shift 2
      ;;
    --socket-file)
      socket_file="$2"
      shift 2
      ;;
    --sessions-file)
      sessions_file="$2"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

log() {
  echo "[swarm-cockpit] $*" >&2
}

if [[ ! -f "${config_file}" ]]; then
  echo "Config file not found: ${config_file}" >&2
  exit 1
fi

if [[ ! -f "${socket_file}" ]]; then
  echo "tmux socket file not found: ${socket_file}" >&2
  echo "Start SwarmForge first, then rerun this script." >&2
  exit 1
fi

socket="$(<"${socket_file}")"
if [[ -z "${socket}" ]]; then
  echo "tmux socket path is empty in ${socket_file}" >&2
  exit 1
fi

if [[ "${socket}" != /* ]]; then
  socket_dir="$(cd "$(dirname "${socket_file}")" && pwd)"
  socket="${socket_dir}/${socket}"
fi

log "base_url=${base_url}"
log "config_file=${config_file}"
log "socket_file=${socket_file}"
log "sessions_file=${sessions_file}"
log "resolved_socket=${socket}"

discover_cockpit_base_url() {
  local -a candidate_ports
  local -a discovered_ports

  # Prefer known defaults first.
  candidate_ports=(5959 5057 5036)

  if command -v ss >/dev/null 2>&1; then
    mapfile -t discovered_ports < <(
      ss -ltnH 2>/dev/null | awk '{print $4}' | sed -E 's/.*:([0-9]+)$/\1/' | awk '/^[0-9]+$/' | awk '!seen[$0]++'
    )
  elif command -v netstat >/dev/null 2>&1; then
    mapfile -t discovered_ports < <(
      netstat -ltn 2>/dev/null | awk 'NR>2 {print $4}' | sed -E 's/.*:([0-9]+)$/\1/' | awk '/^[0-9]+$/' | awk '!seen[$0]++'
    )
  fi

  for port in "${discovered_ports[@]}"; do
    if [[ " ${candidate_ports[*]} " != *" ${port} "* ]]; then
      candidate_ports+=("${port}")
    fi
  done

  log "autodiscovery candidate ports: ${candidate_ports[*]}"

  for port in "${candidate_ports[@]}"; do
    local candidate="http://localhost:${port}"
    if curl -fsS --max-time 1 "${candidate}/api/agents/status" >/dev/null 2>&1; then
      echo "${candidate}"
      return 0
    fi
  done

  return 1
}

if [[ "${base_url_explicit}" -eq 0 ]]; then
  if discovered_base_url="$(discover_cockpit_base_url)"; then
    base_url="${discovered_base_url}"
    log "autodiscovered base_url=${base_url}"
  fi
fi

if ! curl -fsS "${base_url}/api/agents/status" >/dev/null 2>&1; then
  echo "Cannot reach Swarm Cockpit API at ${base_url}" >&2
  echo "Start the service with: bash ./swarm-cockpit start" >&2
  echo "Or rerun with: bash ./swarm-cockpit enable-logs --base-url http://localhost:<port>" >&2
  exit 1
fi

if ! tmux -S "${socket}" list-panes -a >/dev/null 2>&1; then
  echo "Cannot access tmux panes via socket: ${socket}" >&2
  echo "Ensure SwarmForge is running and the socket path is correct." >&2
  exit 1
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ingest_script="${script_dir}/swarm-ingest-lines.sh"

normalize() {
  echo "$1" | tr '[:upper:]' '[:lower:]' | sed 's/[^a-z0-9]/ /g'
}

name_matches_agent() {
  local name_norm
  local agent_norm

  name_norm="$(normalize "$1")"
  agent_norm="$(normalize "$2")"

  if [[ -z "${name_norm// /}" || -z "${agent_norm// /}" ]]; then
    return 1
  fi

  [[ " ${name_norm} " == *" ${agent_norm} "* ]]
}

mapfile -t pane_rows < <(tmux -S "${socket}" list-panes -a -F '#{session_name}|#{window_name}|#{window_index}|#{pane_index}|#{pane_active}|#{pane_current_command}')

declare -A session_by_role
if [[ -f "${sessions_file}" ]]; then
  while IFS=$'\t' read -r idx role session_name _; do
    if [[ -n "${role}" && -n "${session_name}" ]]; then
      session_by_role["${role}"]="${session_name}"
    fi
  done < "${sessions_file}"
fi

echo "[swarm-cockpit] discovered tmux panes:" >&2
for row in "${pane_rows[@]}"; do
  IFS='|' read -r session_name window_name window_index pane_index pane_active pane_cmd <<<"${row}"
  echo "[swarm-cockpit]   ${session_name}:${window_index}.${pane_index} active=${pane_active} window='${window_name}' cmd='${pane_cmd}'" >&2
done
if [[ ${#session_by_role[@]} -gt 0 ]]; then
  echo "[swarm-cockpit] sessions.tsv role -> session mapping:" >&2
  for role in "${!session_by_role[@]}"; do
    echo "[swarm-cockpit]   ${role} -> ${session_by_role[${role}]}" >&2
  done
fi

find_pane_target_for_agent() {
  local agent_name="$1"
  local candidate=""
  local mapped_session="${session_by_role[${agent_name}]:-}"

  # Prefer active pane in mapped SwarmForge session when sessions.tsv is available.
  if [[ -n "${mapped_session}" ]]; then
    for row in "${pane_rows[@]}"; do
      IFS='|' read -r session_name _ window_index pane_index pane_active _ <<<"${row}"
      if [[ "${session_name}" == "${mapped_session}" && "${pane_active}" == "1" ]]; then
        echo "${session_name}:${window_index}.${pane_index}"
        return 0
      fi
    done

    for row in "${pane_rows[@]}"; do
      IFS='|' read -r session_name _ window_index pane_index _ _ <<<"${row}"
      if [[ "${session_name}" == "${mapped_session}" ]]; then
        echo "${session_name}:${window_index}.${pane_index}"
        return 0
      fi
    done
  fi

  for row in "${pane_rows[@]}"; do
    IFS='|' read -r session_name window_name window_index pane_index pane_active pane_cmd <<<"${row}"
    local target="${session_name}:${window_index}.${pane_index}"

    if [[ "${window_name}" == "${agent_name}" || "${session_name}" == "${agent_name}" ]]; then
      echo "${target}"
      return 0
    fi

    if name_matches_agent "${window_name}" "${agent_name}" || name_matches_agent "${session_name}" "${agent_name}"; then
      echo "${target}"
      return 0
    fi

    if [[ -z "${candidate}" || "${pane_active}" == "1" ]]; then
      candidate="${target}"
    fi
  done

  if [[ -n "${candidate}" ]]; then
    log "No direct/fuzzy match for '${agent_name}'. Candidate fallback is ${candidate}."
  fi

  return 1
}

mapfile -t agents < <(
  awk '$1 == "window" { print $2 }' "${config_file}" | sed '/^\s*$/d' | awk '!seen[$0]++'
)

if [[ ${#agents[@]} -eq 0 ]]; then
  echo "No agents found in ${config_file}" >&2
  exit 1
fi

log "agents from config: ${agents[*]}"

skipped=0

for agent in "${agents[@]}"; do
  command="bash '${ingest_script}' --agent '${agent}' --base-url '${base_url}'"

  if target="$(find_pane_target_for_agent "${agent}")"; then
    log "pipe-pane target for ${agent}: ${target}"
    # Always reset first so a previous pipe target (e.g., old port) is replaced.
    tmux -S "${socket}" pipe-pane -t "${target}"
    tmux -S "${socket}" pipe-pane -O -t "${target}" "${command}"
    echo "Enabled log stream for ${agent} on ${target}"

    marker_message="swarm-cockpit stream attached on ${target}"
    marker_payload="{\"message\":\"${marker_message}\",\"stream\":\"system\"}"
    if curl -fsS -X POST "${base_url}/api/agents/${agent}/logs" -H "Content-Type: application/json" -d "${marker_payload}" >/dev/null 2>&1; then
      log "sent marker log for ${agent}"
    else
      echo "[swarm-cockpit] warning: could not send marker log for ${agent}" >&2
    fi
  else
    echo "Skipped ${agent}: no matching tmux pane found"
    skipped=$((skipped + 1))
  fi
done

echo "Done. Open ${base_url} to view live status and logs."
