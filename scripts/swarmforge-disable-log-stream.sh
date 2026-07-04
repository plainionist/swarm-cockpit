#!/usr/bin/env bash
set -euo pipefail

config_file="swarmforge/swarmforge.conf"
socket_file=".swarmforge/tmux-socket"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --config)
      config_file="$2"
      shift 2
      ;;
    --socket-file)
      socket_file="$2"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

if [[ ! -f "${config_file}" ]]; then
  echo "Config file not found: ${config_file}" >&2
  exit 1
fi

if [[ ! -f "${socket_file}" ]]; then
  echo "tmux socket file not found: ${socket_file}" >&2
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

if ! tmux -S "${socket}" list-panes -a >/dev/null 2>&1; then
  echo "Cannot access tmux panes via socket: ${socket}" >&2
  exit 1
fi

find_pane_target_for_agent() {
  local agent_name="$1"
  local candidate=""

  while IFS= read -r line; do
    local target="${line%% *}"
    local window_name="${line#* }"
    local session_name="${target%%:*}"

    if [[ "${window_name}" == "${agent_name}" ]]; then
      echo "${target}"
      return 0
    fi

    if [[ -z "${candidate}" && "${session_name}" == "${agent_name}" ]]; then
      candidate="${target}"
    fi
  done < <(tmux -S "${socket}" list-panes -a -F '#{session_name}:#{window_index}.#{pane_index} #{window_name}')

  if [[ -n "${candidate}" ]]; then
    echo "${candidate}"
    return 0
  fi

  return 1
}

mapfile -t agents < <(
  awk '$1 == "window" { print $2 }' "${config_file}" | sed '/^\s*$/d' | awk '!seen[$0]++'
)

for agent in "${agents[@]}"; do
  if target="$(find_pane_target_for_agent "${agent}")"; then
    tmux -S "${socket}" pipe-pane -t "${target}"
    echo "Disabled log stream for ${agent} on ${target}"
  fi
done
