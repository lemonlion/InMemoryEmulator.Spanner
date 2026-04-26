#!/usr/bin/env pwsh
# ─────────────────────────────────────────────────────────────
#  One-time GCP setup for Cloud Spanner CI integration tests.
#  Run in PowerShell with gcloud CLI installed and authenticated.
#
#  Prerequisites: a GCP billing account (required even for Spanner free tier).
#  If you don't have one, create it at https://console.cloud.google.com/billing
# ─────────────────────────────────────────────────────────────
$ErrorActionPreference = "Stop"

$Project  = "spanner-emulator-ci"
$Instance = "ci-test-instance"
$Region   = "regional-europe-west2"
$SaName   = "spanner-ci-tests"
$SaEmail  = "$SaName@$Project.iam.gserviceaccount.com"

# ── 1/7  Create the GCP project ──
Write-Host "=== 1/7  Creating GCP project '$Project' ===" -ForegroundColor Cyan
gcloud projects create $Project --name="Spanner Emulator CI" 2>$null
if ($LASTEXITCODE -ne 0) { Write-Host "Project already exists, continuing..." }

# ── 2/7  Link billing (required for Spanner, even free tier) ──
Write-Host "=== 2/7  Linking billing account ===" -ForegroundColor Cyan
$BillingAccount = (gcloud billing accounts list --format="value(name)" --filter="open=true" | Select-Object -First 1)
if (-not $BillingAccount) {
    Write-Host "ERROR: No active billing account found." -ForegroundColor Red
    Write-Host "Create one at https://console.cloud.google.com/billing then re-run."
    exit 1
}
Write-Host "Using billing account: $BillingAccount"
gcloud billing projects link $Project --billing-account=$BillingAccount
if ($LASTEXITCODE -ne 0) { throw "Failed to link billing account" }

# ── 3/7  Enable the Spanner API ──
Write-Host "=== 3/7  Enabling Cloud Spanner API ===" -ForegroundColor Cyan
gcloud services enable spanner.googleapis.com --project=$Project
if ($LASTEXITCODE -ne 0) { throw "Failed to enable Spanner API" }

# ── 4/7  Create Spanner instance ──
Write-Host "=== 4/7  Creating Spanner instance (free tier, europe-west2) ===" -ForegroundColor Cyan
gcloud spanner instances create $Instance `
    --config=$Region `
    --description="CI Integration Tests" `
    --instance-type=free-instance `
    --project=$Project
if ($LASTEXITCODE -ne 0) { throw "Failed to create Spanner instance" }

# ── 5/7  Create service account ──
Write-Host "=== 5/7  Creating service account ===" -ForegroundColor Cyan
gcloud iam service-accounts create $SaName `
    --display-name="Spanner CI Integration Tests" `
    --project=$Project
if ($LASTEXITCODE -ne 0) { throw "Failed to create service account" }

# ── 6/7  Grant roles ──
Write-Host "=== 6/7  Granting roles ===" -ForegroundColor Cyan
gcloud projects add-iam-policy-binding $Project `
    --member="serviceAccount:$SaEmail" `
    --role="roles/spanner.databaseAdmin" `
    --condition=None
gcloud projects add-iam-policy-binding $Project `
    --member="serviceAccount:$SaEmail" `
    --role="roles/spanner.databaseUser" `
    --condition=None

# ── 7/7  Export JSON key ──
Write-Host "=== 7/7  Exporting JSON key ===" -ForegroundColor Cyan
$KeyFile = Join-Path $PSScriptRoot "sa-key.json"
gcloud iam service-accounts keys create $KeyFile --iam-account=$SaEmail
if ($LASTEXITCODE -ne 0) { throw "Failed to export key" }

Write-Host ""
Write-Host "=== Done! ===" -ForegroundColor Green
Write-Host ""
Write-Host "Now create these GitHub Actions secrets (Settings > Secrets > Actions > New):"
Write-Host ""
Write-Host "  GCP_SA_KEY          -> paste the FULL contents of sa-key.json below" -ForegroundColor Yellow
Write-Host "  GCP_PROJECT_ID      -> $Project" -ForegroundColor Yellow
Write-Host "  SPANNER_INSTANCE_ID -> $Instance" -ForegroundColor Yellow
Write-Host ""
Write-Host "--- sa-key.json contents (copy everything between the lines) ---" -ForegroundColor Cyan
Write-Host "----------------------------------------------------------------"
Get-Content $KeyFile | Write-Host
Write-Host "----------------------------------------------------------------"
Write-Host ""
Write-Host "Delete the local key file now:" -ForegroundColor Red
Write-Host "  Remove-Item $KeyFile"
