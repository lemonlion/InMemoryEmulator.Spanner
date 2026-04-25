#!/usr/bin/env pwsh
param(
    [string]$ProjectId = "test-project",
    [string]$InstanceId = "test-instance",
    [string]$DatabaseId = "test-db",
    [int]$GrpcPort = 9010,
    [int]$RestPort = 9020
)

$containerName = "spanner-emulator"

# Stop existing container if running
docker rm -f $containerName 2>$null

# Start emulator
docker run -d `
    --name $containerName `
    -p "${GrpcPort}:9010" `
    -p "${RestPort}:9020" `
    gcr.io/cloud-spanner-emulator/emulator

# Wait for emulator to be ready
$maxRetries = 30
$retryCount = 0
while ($retryCount -lt $maxRetries) {
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:${RestPort}/" -TimeoutSec 2 -ErrorAction Stop
        if ($response.StatusCode -eq 200) { break }
    } catch { }
    Start-Sleep -Seconds 1
    $retryCount++
}

if ($retryCount -eq $maxRetries) {
    Write-Error "Spanner emulator did not start within ${maxRetries} seconds"
    exit 1
}

# Set environment for gcloud commands
$env:SPANNER_EMULATOR_HOST = "localhost:${GrpcPort}"

# Create instance + database via REST API
$instanceBody = @{
    instanceId = $InstanceId
    instance = @{
        config = "projects/${ProjectId}/instanceConfigs/emulator-config"
        displayName = "Test Instance"
        nodeCount = 1
    }
} | ConvertTo-Json -Depth 5

Invoke-RestMethod -Method Post `
    -Uri "http://localhost:${RestPort}/v1/projects/${ProjectId}/instances" `
    -Body $instanceBody -ContentType "application/json"

$dbBody = @{
    createStatement = "CREATE DATABASE ``${DatabaseId}``"
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
    -Uri "http://localhost:${RestPort}/v1/projects/${ProjectId}/instances/${InstanceId}/databases" `
    -Body $dbBody -ContentType "application/json"

Write-Host "Spanner emulator ready at localhost:${GrpcPort} (gRPC), localhost:${RestPort} (REST)"
Write-Host "Instance: projects/${ProjectId}/instances/${InstanceId}"
Write-Host "Database: projects/${ProjectId}/instances/${InstanceId}/databases/${DatabaseId}"
