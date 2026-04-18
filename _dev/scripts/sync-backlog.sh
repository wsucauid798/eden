#!/usr/bin/env bash
#
# Parse a markdown backlog file and sync its unchecked items into GitHub as
# issues under a Projects v2 board. Idempotent — existing issues (matched by
# exact title) are skipped.
#
# Usage:
#   PROJECT_NUMBER=<n> ./sync-backlog.sh <path-to-backlog.md> [<path-to-backlog.md> ...]
#
# Environment:
#   PROJECT_NUMBER   required — your GitHub Projects v2 number
#                    find with: gh project list --owner <you>
#   REPO             optional — defaults to the current repo (gh repo view)
#   OWNER            optional — defaults to the repo owner
#   DRY_RUN          optional — set to 1 to print actions without creating
#
# Requires:
#   gh (authenticated with `repo` and `project` scopes: gh auth refresh -s project)
#
# Conventions:
#   - Section headers (## Phase 0 — Groundwork) become labels (phase-0-groundwork).
#   - Unchecked items (- [ ] text) become issues; checked items (- [x]) are skipped.
#   - Matching is by exact title, so don't rename items after seeding unless you
#     want duplicates. Prefer storing the issue number in the backlog:
#       - [ ] #42 Item text
#

set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "usage: $0 <path-to-backlog.md> [<path-to-backlog.md> ...]" >&2
  exit 2
fi

: "${PROJECT_NUMBER:?set PROJECT_NUMBER to your Projects v2 number}"

if ! command -v gh >/dev/null 2>&1; then
  echo "error: gh (GitHub CLI) not found on PATH" >&2
  exit 1
fi

REPO="${REPO:-$(gh repo view --json nameWithOwner -q .nameWithOwner)}"
# Default to the authenticated PAT user. Deriving from the repo owner's login
# fails with "unknown owner type" when gh cannot resolve the login via the
# GraphQL typename query (common for user accounts). Override explicitly if
# the project lives under a different user or organisation.
OWNER="${OWNER:-@me}"
DRY_RUN="${DRY_RUN:-0}"

slugify() {
  echo "$1" \
    | tr '[:upper:]' '[:lower:]' \
    | sed -E 's/[^a-z0-9]+/-/g; s/^-+|-+$//g' \
    | cut -c1-48
}

ensure_label() {
  local label="$1"
  [[ -z "$label" ]] && return
  gh label create "$label" --color ededed --force >/dev/null 2>&1 || true
}

issue_exists() {
  local title="$1"
  local count
  count=$(gh issue list --repo "$REPO" --state all --limit 200 \
            --search "\"$title\" in:title" --json title \
            --jq "[.[] | select(.title == \"$title\")] | length")
  [[ "$count" -gt 0 ]]
}

create_issue_and_add() {
  local title="$1" phase="$2" file="$3"
  local label
  label="$(slugify "$phase")"
  local body
  body="From [\`$file\`]($file) — **$phase**"

  if [[ "$DRY_RUN" == "1" ]]; then
    echo "[dry-run] would create: $title (label=$label)"
    return
  fi

  ensure_label "$label"
  local url
  url=$(gh issue create --repo "$REPO" --title "$title" \
          ${label:+--label "$label"} --body "$body")
  gh project item-add "$PROJECT_NUMBER" --owner "$OWNER" --url "$url" >/dev/null
  echo "added: $title -> $url"
}

process_file() {
  local file="$1"
  if [[ ! -f "$file" ]]; then
    echo "skip (not found): $file" >&2
    return
  fi

  echo "=== $file ==="
  local current_phase=""

  while IFS= read -r line || [[ -n "$line" ]]; do
    # Section header becomes the current phase label
    if [[ "$line" =~ ^##[[:space:]]+(.*)$ ]]; then
      current_phase="${BASH_REMATCH[1]}"
      continue
    fi

    # Unchecked item: - [ ] text
    if [[ "$line" =~ ^[[:space:]]*-[[:space:]]\[[[:space:]]\][[:space:]]+(.+)$ ]]; then
      local title="${BASH_REMATCH[1]}"
      # Strip leading "#42 " issue-number prefix if already seeded
      title="${title#\#[0-9]* }"
      # Trim trailing whitespace
      title="${title%"${title##*[![:space:]]}"}"

      if issue_exists "$title"; then
        echo "exists: $title"
        continue
      fi

      create_issue_and_add "$title" "$current_phase" "$file"
    fi
  done < "$file"
}

for f in "$@"; do
  process_file "$f"
done
