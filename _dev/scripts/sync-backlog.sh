#!/usr/bin/env bash
#
# Parse markdown backlog files and sync their unchecked items into GitHub as
# issues *and* add them to a Projects v2 board. Idempotent on both sides:
#   - issues whose title already exists in the repo are not re-created
#   - issues whose URL is already in the project are not re-added
# Orphan issues (exist in repo but not in project) are swept into the project.
#
# Usage:
#   PROJECT_NUMBER=<n> ./sync-backlog.sh <file.md> [<file.md> ...]
#
# Environment:
#   PROJECT_NUMBER   required — GitHub Projects v2 number
#   REPO             optional — defaults to the current repo
#   OWNER            optional — defaults to @me
#   DRY_RUN          optional — set to 1 to print actions without mutating
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

issues_created=0
project_added=0
skipped=0
closed=0
failures=0

# ---------- helpers ----------

slugify() {
  echo "$1" \
    | tr '[:upper:]' '[:lower:]' \
    | sed -E 's/[^a-z0-9]+/-/g; s/^-+|-+$//g' \
    | cut -c1-48
}

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

# ---------- caches ----------

echo "Loading existing issues from $REPO…"
existing_issues_json="$(gh issue list --repo "$REPO" --state all --limit 1000 \
                         --json title,url,number,state 2>/dev/null || echo '[]')"

echo "Loading existing project items from project #$PROJECT_NUMBER…"
existing_project_json="$(gh project item-list "$PROJECT_NUMBER" --owner "$OWNER" \
                          --format json --limit 1000 2>/dev/null \
                          || echo '{"items":[]}')"
existing_project_urls="$(jq -r '.items[]?.content.url // empty' \
                         <<<"$existing_project_json" | sort -u)"

find_issue_url() {
  local title="$1"
  jq -r --arg t "$title" \
     'map(select(.title == $t) | .url) | .[0] // empty' \
     <<<"$existing_issues_json"
}

# For close-on-tick, match exactly first; if not found, fall back to a
# prefix match. The backlog may use ~~short title~~ where the full issue
# title has trailing detail (e.g. "(domain types: Vector3, ...)").
find_issue_number() {
  local title="$1"
  local n
  n=$(jq -r --arg t "$title" \
       'map(select(.title == $t) | .number) | .[0] // empty' \
       <<<"$existing_issues_json")
  if [[ -z "$n" ]]; then
    n=$(jq -r --arg t "$title" \
         'map(select(.title | startswith($t)) | .number) | .[0] // empty' \
         <<<"$existing_issues_json")
  fi
  echo "$n"
}

find_issue_state() {
  local title="$1"
  local s
  s=$(jq -r --arg t "$title" \
       'map(select(.title == $t) | .state) | .[0] // empty' \
       <<<"$existing_issues_json")
  if [[ -z "$s" ]]; then
    s=$(jq -r --arg t "$title" \
         'map(select(.title | startswith($t)) | .state) | .[0] // empty' \
         <<<"$existing_issues_json")
  fi
  echo "$s"
}

url_in_project() {
  local url="$1"
  [[ -z "$url" ]] && return 1
  grep -Fxq "$url" <<<"$existing_project_urls"
}

remember_issue() {
  local title="$1" url="$2"
  existing_issues_json="$(jq --arg t "$title" --arg u "$url" \
                           '. + [{title: $t, url: $u}]' \
                           <<<"$existing_issues_json")"
}

remember_project_url() {
  local url="$1"
  existing_project_urls="$(printf '%s\n%s\n' "$existing_project_urls" "$url" | sort -u)"
}

# ---------- per-item work ----------

sync_item() {
  local title="$1" phase="$2" file="$3"
  local label url body
  label="$(slugify "$phase")"
  body="From [\`$file\`]($file) — **$phase**"

  url="$(find_issue_url "$title")"

  # 1. Create the issue if it doesn't exist yet.
  if [[ -z "$url" ]]; then
    if [[ "$DRY_RUN" == "1" ]]; then
      echo "[dry-run] would create issue: $title"
      url="dry-run://$title"
    else
      ensure_label "$label"
      if ! url=$(retry gh issue create --repo "$REPO" --title "$title" \
                        ${label:+--label "$label"} --body "$body"); then
        echo "FAIL create issue: $title" >&2
        return 1
      fi
      echo "issue: $title -> $url"
      remember_issue "$title" "$url"
      issues_created=$((issues_created + 1))
    fi
  fi

  # 2. Add to project if it's not already there.
  if url_in_project "$url"; then
    skipped=$((skipped + 1))
    return 0
  fi

  if [[ "$DRY_RUN" == "1" ]]; then
    echo "[dry-run] would add to project: $title ($url)"
    return 0
  fi

  if ! retry gh project item-add "$PROJECT_NUMBER" \
               --owner "$OWNER" --url "$url" >/dev/null; then
    echo "FAIL project-add: $title ($url)" >&2
    return 1
  fi

  echo "project: $title"
  remember_project_url "$url"
  project_added=$((project_added + 1))
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
      title="${title#\#[0-9]* }"
      title="${title%"${title##*[![:space:]]}"}"

      if ! sync_item "$title" "$current_phase" "$file"; then
        failures=$((failures + 1))
      fi
    elif [[ "$line" =~ ^[[:space:]]*-[[:space:]]\[x\][[:space:]]+(.+)$ ]]; then
      # Checked item — close the matching GitHub issue if still open.
      local raw="${BASH_REMATCH[1]}"
      # Strip ~~...~~ struck-through text wrapping if present, keep core title.
      local title="${raw#~~}"; title="${title%%~~*}"
      title="${title#\#[0-9]* }"
      title="${title%"${title##*[![:space:]]}"}"

      local number; number="$(find_issue_number "$title")"
      local state;  state="$(find_issue_state  "$title")"
      if [[ -n "$number" && "$state" == "OPEN" ]]; then
        if [[ "$DRY_RUN" == "1" ]]; then
          echo "[dry-run] would close #$number: $title"
        elif retry gh issue close "$number" --repo "$REPO" --reason completed >/dev/null; then
          echo "closed #$number: $title"
          closed=$((closed + 1))
        else
          echo "FAIL close #$number: $title" >&2
          failures=$((failures + 1))
        fi
      fi
    fi
  done < "$file"
}

for f in "$@"; do
  process_file "$f"
done

echo
echo "summary: issues_created=$issues_created project_added=$project_added closed=$closed skipped=$skipped failures=$failures"

[[ "$failures" -eq 0 ]]
