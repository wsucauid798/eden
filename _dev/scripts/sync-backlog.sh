#!/usr/bin/env bash
#
# One-way sync: backlog markdown -> GitHub Issues + Projects v2 board.
#
# What it does:
#   - For each `- [ ]` line without `#NNN`: create the issue, insert
#     `#NNN` into the line, add to the project.
#   - For each `- [ ]` line with `#NNN`: ensure it's in the project.
#   - For each `- [x]` line: **skip** — closure is the user's manual
#     action on GitHub. The backlog being ticked is a local signal,
#     nothing more.
#
# Legacy lines without `#NNN` try a title prefix-match against existing
# issues before creating, so prior issues get linked instead of duplicated.
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
linked=0
skipped=0
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

issue_url_by_number() {
  local number="$1"
  jq -r --argjson n "$number" \
     'map(select(.number == $n) | .url) | .[0] // empty' \
     <<<"$existing_issues_json"
}

issue_state_by_number() {
  local number="$1"
  jq -r --argjson n "$number" \
     'map(select(.number == $n) | .state) | .[0] // empty' \
     <<<"$existing_issues_json"
}

# Legacy migration path: find an issue by title prefix when the backlog
# line doesn't yet have a #NNN.
find_issue_number_by_title() {
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

url_in_project() {
  local url="$1"
  [[ -z "$url" ]] && return 1
  grep -Fxq "$url" <<<"$existing_project_urls"
}

remember_issue() {
  local number="$1" title="$2" url="$3" state="$4"
  existing_issues_json="$(jq --argjson n "$number" --arg t "$title" --arg u "$url" --arg s "$state" \
                           '. + [{number: $n, title: $t, url: $u, state: $s}]' \
                           <<<"$existing_issues_json")"
}

remember_project_url() {
  local url="$1"
  existing_project_urls="$(printf '%s\n%s\n' "$existing_project_urls" "$url" | sort -u)"
}

# ---------- actions ----------

create_issue() {
  local title="$1" phase="$2" file="$3"
  local label body
  label="$(slugify "$phase")"
  body="From [\`$file\`]($file) — **$phase**"

  if [[ "$DRY_RUN" == "1" ]]; then
    echo "[dry-run] would create: $title"
    echo "999999"  # fake number, caller handles dry-run
    return 0
  fi

  ensure_label "$label"

  local url
  if ! url=$(retry gh issue create --repo "$REPO" --title "$title" \
                    ${label:+--label "$label"} --body "$body"); then
    return 1
  fi

  # Parse number out of the URL (https://github.com/owner/repo/issues/NNN).
  local number="${url##*/}"
  remember_issue "$number" "$title" "$url" "OPEN"
  echo "$number"
  return 0
}

add_to_project() {
  local number="$1" title="$2"
  local url; url="$(issue_url_by_number "$number")"
  [[ -z "$url" ]] && return 1
  if url_in_project "$url"; then return 0; fi

  if [[ "$DRY_RUN" == "1" ]]; then
    echo "[dry-run] would add to project: #$number $title"
    return 0
  fi

  if ! retry gh project item-add "$PROJECT_NUMBER" \
               --owner "$OWNER" --url "$url" >/dev/null; then
    return 1
  fi
  remember_project_url "$url"
  return 0
}

# ---------- per-file processing ----------

# Rewrites the file in-place, inserting `#NNN ` after the checkbox for any
# newly-created issues. Writes back only if any lines changed.
process_file() {
  local file="$1"
  if [[ ! -f "$file" ]]; then
    echo "skip (not found): $file" >&2
    return
  fi

  echo "=== $file ==="

  local -a lines
  mapfile -t lines < "$file"

  local current_phase=""
  local file_changed=0

  local i
  for (( i = 0; i < ${#lines[@]}; i++ )); do
    local line="${lines[$i]}"

    # Track section headers (phase labels).
    if [[ "$line" =~ ^##[[:space:]]+(.*)$ ]]; then
      current_phase="${BASH_REMATCH[1]}"
      continue
    fi

    # Unchecked line: - [ ] [#NNN] title
    if [[ "$line" =~ ^([[:space:]]*-[[:space:]]\[)[[:space:]](\][[:space:]]+)(.+)$ ]]; then
      local prefix="${BASH_REMATCH[1]}"
      local closer="${BASH_REMATCH[2]}"
      local body="${BASH_REMATCH[3]}"
      process_open_line "$i" "$prefix" "$closer" "$body" "$current_phase" "$file"
      continue
    fi

    # Checked line: skip — closure is manual on GitHub.
    if [[ "$line" =~ ^[[:space:]]*-[[:space:]]\[x\][[:space:]]+ ]]; then
      skipped=$((skipped + 1))
      continue
    fi
  done

  if (( file_changed )); then
    printf '%s\n' "${lines[@]}" > "$file"
    echo "updated: $file (embedded issue numbers)"
  fi
}

# Process an unchecked `- [ ]` line. Arguments:
#   $1 index  $2 "- [ ["  $3 "] "  $4 body (may start with #NNN)  $5 phase  $6 file
# Sets file_changed=1 and rewrites lines[$i] if we created an issue.
process_open_line() {
  local idx="$1" prefix="$2" closer="$3" body="$4" phase="$5" file="$6"

  local number="" title="$body"
  if [[ "$body" =~ ^#([0-9]+)[[:space:]]+(.*)$ ]]; then
    number="${BASH_REMATCH[1]}"
    title="${BASH_REMATCH[2]}"
  fi

  if [[ -z "$number" ]]; then
    # No number yet — try title match first (migration), else create.
    number="$(find_issue_number_by_title "$title")"
    if [[ -z "$number" ]]; then
      if ! number=$(create_issue "$title" "$phase" "$file"); then
        echo "FAIL create: $title" >&2
        failures=$((failures + 1))
        return
      fi
      issues_created=$((issues_created + 1))
      echo "issue #$number: $title"
    else
      echo "link #$number: $title"
      linked=$((linked + 1))
    fi
    # Rewrite the line with #NNN embedded.
    lines[$idx]="${prefix} ${closer}#${number} ${title}"
    file_changed=1
  fi

  # Make sure issue is in the project.
  if add_to_project "$number" "$title"; then
    local url; url="$(issue_url_by_number "$number")"
    if [[ -n "$url" ]] && ! url_in_project "$url"; then
      echo "project: #$number $title"
      project_added=$((project_added + 1))
    fi
  else
    echo "FAIL project-add: #$number $title" >&2
    failures=$((failures + 1))
  fi
}

for f in "$@"; do
  process_file "$f"
done

echo
echo "summary: issues_created=$issues_created linked=$linked project_added=$project_added skipped=$skipped failures=$failures"

[[ "$failures" -eq 0 ]]
