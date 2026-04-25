#!/usr/bin/env pwsh

docker rm -f spanner-emulator 2>$null
Write-Host "Spanner emulator stopped"
