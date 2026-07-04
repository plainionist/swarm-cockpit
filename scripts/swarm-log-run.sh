#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "Usage: swarm-log-run.sh --agent <name> [--base-url <url>] -- <command...>" >&2
  exit 1
fi

agent=""
base_url="http://localhost:5959"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --agent)
      agent="$2"
      shift 2
      ;;
    --base-url)
      base_url="$2"
      shift 2
      ;;
    --)
      shift
      break
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

if [[ -z "${agent}" ]]; then
  echo "--agent is required" >&2
  exit 1
fi

if [[ $# -eq 0 ]]; then
  echo "No command specified after --" >&2
  exit 1
fi

set -o pipefail
"$@" 2>&1 | while IFS= read -r line; do
  printf '%s\n' "$line"
  escaped_line="${line//\\/\\\\}"
  escaped_line="${escaped_line//\"/\\\"}"
  curl -sS -X POST "${base_url}/api/agents/${agent}/logs" \
    -H "Content-Type: application/json" \
    -d "{\"message\":\"${escaped_line}\",\"stream\":\"stdout\"}" >/dev/null || true
done

exit ${PIPESTATUS[0]}
