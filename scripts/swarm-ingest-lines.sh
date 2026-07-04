#!/usr/bin/env bash
set -euo pipefail

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

while IFS= read -r line; do
  # Remove ANSI escape sequences and non-printable control chars that break JSON payloads.
  clean_line="$(printf '%s' "${line}" | sed -E $'s/\x1B\[[0-9;?]*[ -/]*[@-~]//g' | tr -d '\000-\037')"

  # Keep log tail readable: drop non-ASCII glyph fragments (spinner/progress symbols, mojibake).
  clean_line="$(printf '%s' "${clean_line}" | LC_ALL=C tr -cd '\040-\176')"

  # Collapse excessive spaces introduced by glyph stripping.
  clean_line="$(printf '%s' "${clean_line}" | sed -E 's/[[:space:]]+/ /g; s/^ //; s/ $//')"

  if [[ -z "${clean_line}" ]]; then
    continue
  fi

  escaped_line="${clean_line//\\/\\\\}"
  escaped_line="${escaped_line//\"/\\\"}"

  if ! curl -fsS -X POST "${base_url}/api/agents/${agent}/logs" \
    -H "Content-Type: application/json" \
    -d "{\"message\":\"${escaped_line}\",\"stream\":\"stdout\"}" >/dev/null; then
    echo "[swarm-cockpit] warning: failed to post log line for ${agent}" >&2
  fi
done
