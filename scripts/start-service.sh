#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

cd "${REPO_ROOT}"

dotnet restore

# WSL2 only: refresh the Windows port-proxy so LAN clients can reach this
# service at the current (NAT'd, volatile) WSL IP. No-op outside WSL or if the
# scheduled task has not been installed. See scripts/windows/*.ps1 and README.
if command -v schtasks.exe >/dev/null 2>&1; then
  schtasks.exe /run /tn "SwarmCockpitPortProxy" >/dev/null 2>&1 || true
fi

exec dotnet run --no-launch-profile --project src/SwarmCockpit.Service "$@"
