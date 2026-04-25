<#
.SYNOPSIS
    Runs integration tests against the Go Spanner emulator from WSL2 Ubuntu.
    This works around Rancher Desktop's h2c port-forwarding limitation.

.DESCRIPTION
    Rancher Desktop's WSL2-to-Windows port forwarding breaks HTTP/2 cleartext (h2c)
    which the Go Spanner emulator requires for gRPC. Running 'dotnet vstest' from
    inside WSL2 Ubuntu bypasses this issue by connecting directly through the Linux
    networking stack.

.PARAMETER Filter
    Optional test filter expression (TestCaseFilter syntax).

.PARAMETER EmulatorPort
    The gRPC port of the emulator container. Default: 19010.

.PARAMETER RestPort
    The REST port of the emulator container. Default: 19020.

.EXAMPLE
    .\scripts\run-emulator-tests-wsl.ps1
    .\scripts\run-emulator-tests-wsl.ps1 -Filter "SelectLiteral_ReturnsValue"
#>
param(
    [string]$Filter = "",
    [int]$EmulatorPort = 19010,
    [int]$RestPort = 19020
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$testDll = "tests/Spanner.InMemoryEmulator.Tests.Integration/bin/Debug/net8.0/Spanner.InMemoryEmulator.Tests.Integration.dll"
$testDllFull = Join-Path $repoRoot $testDll

# 1. Ensure the test DLL is built
Write-Host "Building test project..." -ForegroundColor Cyan
dotnet build "$repoRoot/tests/Spanner.InMemoryEmulator.Tests.Integration" -v q
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

# 2. Ensure emulator container is running
$containerName = "spanner-emu-ubuntu"
$existing = docker ps -q --filter "name=$containerName" 2>$null
if (-not $existing) {
    Write-Host "Starting emulator container ($containerName)..." -ForegroundColor Cyan
    docker rm -f $containerName 2>$null
    docker run -d --name $containerName -p "${EmulatorPort}:9010" -p "${RestPort}:9020" gcr.io/cloud-spanner-emulator/emulator
    Start-Sleep -Seconds 3

    # Create instance and database
    $instBody = '{"instanceId":"test-instance","instance":{"config":"emulator-config","displayName":"Test","nodeCount":1}}'
    try { Invoke-RestMethod -Uri "http://localhost:${RestPort}/v1/projects/test-project/instances" -Method POST -Body $instBody -ContentType "application/json" -TimeoutSec 10 } catch {}
    $dbBody = '{"createStatement":"CREATE DATABASE `test-db`"}'
    try { Invoke-RestMethod -Uri "http://localhost:${RestPort}/v1/projects/test-project/instances/test-instance/databases" -Method POST -Body $dbBody -ContentType "application/json" -TimeoutSec 10 } catch {}
    Write-Host "Emulator started and configured." -ForegroundColor Green
}

# 3. Run tests from WSL2 Ubuntu
$wslDll = "/mnt/c" + ($testDllFull.Substring(2) -replace '\\', '/')
$filterArg = if ($Filter) { "--TestCaseFilter:`"$Filter`"" } else { "--TestCaseFilter:`"Target!=InMemoryOnly`"" }

Write-Host "Running tests from WSL2 Ubuntu against emulator on port $EmulatorPort..." -ForegroundColor Cyan
Write-Host "  DLL: $wslDll" -ForegroundColor DarkGray
Write-Host "  Filter: $filterArg" -ForegroundColor DarkGray

wsl -d Ubuntu -- bash -c "SPANNER_TEST_TARGET=Emulator SPANNER_EMULATOR_HOST=localhost:${EmulatorPort} dotnet vstest '$wslDll' $filterArg 2>&1"
