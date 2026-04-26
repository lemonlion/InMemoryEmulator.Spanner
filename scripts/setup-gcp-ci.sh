#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────
#  One-time GCP setup for Cloud Spanner CI integration tests.
#  Paste this into Google Cloud Shell (https://shell.cloud.google.com)
#  or run locally with gcloud CLI authenticated.
#
#  Prerequisites: a GCP billing account (required even for Spanner free tier).
#  If you don't have one, create it at https://console.cloud.google.com/billing
# ─────────────────────────────────────────────────────────────
set -euo pipefail

PROJECT="spanner-emulator-ci"
INSTANCE="ci-test-instance"
REGION="regional-europe-west2"
SA_NAME="spanner-ci-tests"
SA_EMAIL="${SA_NAME}@${PROJECT}.iam.gserviceaccount.com"

# ── 1/7  Create the GCP project ──
echo "=== 1/7  Creating GCP project '$PROJECT' ==="
gcloud projects create "$PROJECT" --name="Spanner Emulator CI" 2>/dev/null || echo "Project already exists, continuing..."

# ── 2/7  Link billing (required for Spanner, even free tier) ──
echo "=== 2/7  Linking billing account ==="
BILLING_ACCOUNT=$(gcloud billing accounts list --format="value(name)" --filter="open=true" | head -1)
if [ -z "$BILLING_ACCOUNT" ]; then
  echo "ERROR: No active billing account found."
  echo "Create one at https://console.cloud.google.com/billing then re-run."
  exit 1
fi
echo "Using billing account: $BILLING_ACCOUNT"
gcloud billing projects link "$PROJECT" --billing-account="$BILLING_ACCOUNT"

# ── 3/7  Enable the Spanner API ──
echo "=== 3/7  Enabling Cloud Spanner API ==="
gcloud services enable spanner.googleapis.com --project="$PROJECT"

echo "=== 4/7  Creating Spanner instance (free tier, europe-west2) ==="
gcloud spanner instances create "$INSTANCE" \
  --config="$REGION" \
  --description="CI Integration Tests" \
  --instance-type=free-instance \
  --project="$PROJECT"

echo "=== 5/7  Creating service account ==="
gcloud iam service-accounts create "$SA_NAME" \
  --display-name="Spanner CI Integration Tests" \
  --project="$PROJECT"

echo "=== 6/7  Granting roles ==="
gcloud projects add-iam-policy-binding "$PROJECT" \
  --member="serviceAccount:$SA_EMAIL" \
  --role="roles/spanner.databaseAdmin" \
  --condition=None

gcloud projects add-iam-policy-binding "$PROJECT" \
  --member="serviceAccount:$SA_EMAIL" \
  --role="roles/spanner.databaseUser" \
  --condition=None

echo "=== 7/7  Exporting JSON key ==="
gcloud iam service-accounts keys create sa-key.json \
  --iam-account="$SA_EMAIL"

echo ""
echo "=== Done! ==="
echo ""
echo "Now create these GitHub Actions secrets (Settings → Secrets → Actions → New):"
echo ""
echo "  GCP_SA_KEY          → paste the FULL contents of sa-key.json below"
echo "  GCP_PROJECT_ID      → $PROJECT"
echo "  SPANNER_INSTANCE_ID → $INSTANCE"
echo ""
echo "─── sa-key.json contents (copy everything between the lines) ───"
echo "────────────────────────────────────────────────────────────────"
cat sa-key.json
echo ""
echo "────────────────────────────────────────────────────────────────"
echo ""
echo "⚠  Delete the local key file now:"
echo "   rm sa-key.json"
