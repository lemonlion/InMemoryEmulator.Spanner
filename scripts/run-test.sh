#!/bin/bash
# Run integration tests against the Go Spanner emulator via WSL2.
# Requires: Docker container with gcr.io/cloud-spanner-emulator/emulator on ports 19010/19020
# Usage from PowerShell: wsl -d Ubuntu -- bash scripts/run-emulator-tests.sh [test-filter]
set -euo pipefail

export SPANNER_TEST_TARGET=Emulator
export SPANNER_EMULATOR_HOST=localhost:19010

DLL_PATH="/mnt/c/git/InMemoryEmulator.Spanner/tests/InMemoryEmulator.Spanner.Tests.Integration/bin/Debug/net8.0/InMemoryEmulator.Spanner.Tests.Integration.dll"

FILTER="${1:-Target!=InMemoryOnly&Target!=GoEmulatorUnsupported}"

echo "Running integration tests against Go emulator..."
echo "Filter: $FILTER"
echo ""

dotnet vstest "$DLL_PATH" \
  --TestCaseFilter:"$FILTER" \
  --logger:"console;verbosity=normal" 2>&1
