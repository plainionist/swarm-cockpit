#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

cd "${REPO_ROOT}"

# Keep the SQLite database in <repo>/data, never inside src.
export Persistence__ConnectionString="${Persistence__ConnectionString:-Data Source=${REPO_ROOT}/data/swarm-cockpit.db}"
mkdir -p "${REPO_ROOT}/data"

dotnet restore
exec dotnet run --no-launch-profile --project src/SwarmCockpit.Service "$@"
