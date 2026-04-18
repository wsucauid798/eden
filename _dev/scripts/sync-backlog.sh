#!/usr/bin/env bash
#
# Parse markdown backlog files and sync their unchecked items into GitHub as
# issues under a Projects v2 board. Idempotent — issues whose title already
# exists in the repo (any state) are skipped.
#
# Usage:
#   PROJECT_NUMBER=<n> ./sync-backlog.sh <file.md> [<file.md> ...]
#
# Environment:
#   PROJECT_NUMBER   required — your GitHub Projects v2 number
#   REPO             optional — defaults to the current repo
#   OWNER            optional — defaults to @me (the authenticated PAT user)
#   DRY_RUN          optional — set to 1 to print actions without creating
#
# Conventions:
#   - Section headers (## Phase 0 — Groundwork) become labels.
#   - Unchecked items (- [ ] text) become issues; checked items (- [x]) skip.
#   - Matching is by exact title; do not rename items after seeding.
#

set -uo pipefail

if [[ $# -lt 1 ]]; then
  echo "usage: $0 <file.md> [<file.md> ...]" >&2
  exit 2
fi

: "${PROJECT_NUMBER:?set PROJECT_NUMBER to your Projects v2 number}"

command -v gh >/dev/null 2>&1 || { echo "error: gh not found" >&2; exit 1; }
command -v jq >/dev/null 2>&1 || { echo "error: jq not found" >&2; exit 1; }

REPO="${REPO:-$(gh repo view --json nameWithOwner -q .nameWithOwner)}"
OWNER="${OWNER:-@me}"
DRY_RUN="${DRY_RUN:-0}"

created=0
skipped=0
failures=0

# ---------- helpers ----------

slugify() {
  echo "$1" \
    | tr '[:upper:]' '[:lower:]' \
    | sed -E 's/[^a-z0-9]+/-/g; s/^-+|-+$//g' \
    | cut -c1-48
}

# Retry a command up to N times with exponential backoff on failure.
retry() {
  local attempts=3 i=0 sleep_s=2
  while (( i < attempts )); do
    if "$@"; then return 0; fi
    i=$((i + 1))
    if (( i < attempts )); then
      echo "  retry $i/$((attempts - 1)) after ${sleep_s}s…" >&2
      sleep "$sleep_s"
      sleep_s=$((sleep_s * 2))
    fi
  done
  return 1
}

ensure_label() {
  local label="$1"
  [[ -z "$label" ]] && return
  gh label create "$label" --color ededed --force --repo "$REPO" >/dev/null 2>&1 || true
}

# ---------- load existing issues once ----------

echo "Loading existing issues from $REPO…"
existing_titles_json="$(gh issue list --repo "$REPO" --state all --limit 1000 \
                         --json title 2>/dev/null || echo '[]')"

issue_exists() {
  local title="$1"
  jq -e --arg t "$title" 'map(.title) | index($t) != null' \
     <<<"$existing_titles_json" >/dev/null 2>&1
}

# After creating an issue, append its title so later items in the same run
# also see it.
remember_issue() {
  local title="$1"
  existing_titles_json="$(jq --arg t "$title" '. + [{title: $t}]' \
                           <<<"$existing_titles_json")"
}

# ---------- per-item work ----------

create_issue_and_add() {
  local title="$1" phase="$2" file="$3"
  local label body url
  label="$(slugify "$phase")"
  body="From [\`$file\`]($file) — **$phase**"

  if [[ "$DRY_RUN" == "1" ]]; then
    echo "[dry-run] would create: $title (label=$label)"
    return 0
  fi

  ensure_label "$label"

  if ! url=$(retry gh issue create --repo "$REPO" --title "$title" \
                    ${label:+--label "$label"} --body "$body"); then
    echo "FAIL create: $title" >&2
    return 1
  fi

  if ! retry gh project item-add "$PROJECT_NUMBER" \
               --owner "$OWNER" --url "$url" >/dev/null; then
    echo "FAIL project-add: $title ($url)" >&2
    # Issue created but not added to project. Still counts as a partial
    # success — user can re-run or add manually.
    remember_issue "$title"
    return 1
  fi

  remember_issue "$title"
  echo "added: $title -> $url"
  return 0
}

# ---------- main loop ----------

process_file() {
  local file="$1"
  if [[ ! -f "$file" ]]; then
    echo "skip (not found): $file" >&2
    return
  fi

  echo "=== $file ==="
  local current_phase=""

  while IFS= read -r line || [[ -n "$line" ]]; do
    if [[ "$line" =~ ^##[[:space:]]+(.*)$ ]]; then
      current_phase="${BASH_REMATCH[1]}"
      continue
    fi

    if [[ "$line" =~ ^[[:space:]]*-[[:space:]]\[[[:space:]]\][[:space:]]+(.+)$ ]]; then
      local title="${BASH_REMATCH[1]}"
      title="${title#\#[0-9]* }"                       # strip "#42 " prefix
      title="${title%"${title##*[![:space:]]}"}"        # rtrim

      if issue_exists "$title"; then
        echo "exists: $title"
        skipped=$((skipped + 1))
        continue
      fi

      if create_issue_and_add "$title" "$current_phase" "$file"; then
        created=$((created + 1))
      else
        failures=$((failures + 1))
      fi
    fi
  done < "$file"
}

for f in "$@"; do
  process_file "$f"
done

echo
echo "summary: created=$created skipped=$skipped failures=$failures"

# Exit non-zero if any item failed, but only after processing everything.
[[ "$failures" -eq 0 ]]
