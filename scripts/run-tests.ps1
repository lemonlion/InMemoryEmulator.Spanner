#!/usr/bin/env pwsh
param(
    [ValidateSet("InMemory", "Emulator", "CloudSpanner")]
    [string]$Target = "InMemory",

    [string]$Filter = "",

    [string]$RunName = "default",

    [switch]$Coverage
)

$env:SPANNER_TEST_TARGET = $Target

$baseArgs = @(
    "test"
    "--configuration", "Release"
    "--logger", "trx;LogFileName=${RunName}.trx"
)

if ($Filter) { $baseArgs += "--filter", $Filter }
if ($Coverage) { $baseArgs += "--collect:XPlat Code Coverage" }

# Unit tests (only in-memory)
if ($Target -eq "InMemory") {
    Write-Host "=== Unit Tests ===" -ForegroundColor Cyan
    dotnet @baseArgs tests/Spanner.InMemoryEmulator.Tests.Unit/
}

# Integration tests
Write-Host "=== Integration Tests ($Target) ===" -ForegroundColor Cyan
$integrationArgs = $baseArgs.Clone()
if ($Target -ne "InMemory") {
    if ($Filter) {
        $integrationArgs = @(
            "test"
            "--configuration", "Release"
            "--logger", "trx;LogFileName=${RunName}.trx"
            "--filter", "(${Filter})&Target!=InMemoryOnly"
        )
    } else {
        $integrationArgs += "--filter", "Target!=InMemoryOnly"
    }
}
dotnet @integrationArgs tests/Spanner.InMemoryEmulator.Tests.Integration/
