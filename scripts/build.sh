#!/usr/bin/env bash
# Eden build script (bash / macOS / Linux).
# Produces runnable binaries under ../build/.
#
# Usage:
#   scripts/build.sh                  # host OS, x64
#   scripts/build.sh linux-x64        # cross-compile target
#   scripts/build.sh --clean          # wipe build/ first

set -euo pipefail

rid=""
clean=0
for arg in "$@"; do
    case "$arg" in
        --clean) clean=1 ;;
        *)       rid="$arg" ;;
    esac
done

if [[ -z "$rid" ]]; then
    case "$(uname -s)" in
        Linux*)  rid="linux-x64" ;;
        Darwin*) rid="osx-arm64" ;;
        MINGW*|MSYS*|CYGWIN*) rid="win-x64" ;;
        *)       rid="linux-x64" ;;
    esac
fi

repo="$(cd "$(dirname "$0")/.." && pwd)"
build_dir="$repo/build"
server_out="$build_dir/eden-server-$rid"

if [[ "$clean" -eq 1 && -d "$build_dir" ]]; then
    echo "cleaning $build_dir"
    rm -rf "$build_dir"
fi
mkdir -p "$build_dir"

echo "publishing Eden.Server (CLI) for $rid -> $server_out"
dotnet publish \
    "$repo/src/Eden.Server.Cli/Eden.Server.Cli.csproj" \
    --configuration Release \
    --runtime "$rid" \
    --self-contained \
    --output "$server_out" \
    --nologo

echo ""
echo "server binary:"
find "$server_out" -maxdepth 1 -type f \( -name 'Eden.Server.Cli' -o -name 'Eden.Server.Cli.exe' \) -print

# ------------------------------------------------------------------
# Viewer export (Godot)
# ------------------------------------------------------------------

godot=""
if [[ -n "${GODOT_BIN:-}" && -x "$GODOT_BIN" ]]; then
    godot="$GODOT_BIN"
fi
if [[ -z "$godot" ]]; then
    for c in godot godot4 Godot Godot_mono \
             Godot_v4.6.2-stable_mono_win64 \
             Godot_v4.6.1-stable_mono_win64 \
             Godot_v4.6.2-stable_mono_linux.x86_64 \
             Godot_v4.6.1-stable_mono_linux.x86_64 \
             "/Applications/Godot_mono.app/Contents/MacOS/Godot"; do
        if command -v "$c" >/dev/null 2>&1; then godot="$c"; break; fi
        if [[ -x "$c" ]]; then godot="$c"; break; fi
    done
fi

if [[ -z "$godot" ]]; then
    echo ""
    echo "godot not found. skipping viewer export."
    echo "set GODOT_BIN=<path> or put godot on PATH."
    exit 0
fi

viewer_project="$repo/src/Eden.Viewer"
viewer_out="$build_dir/eden-viewer-$rid"
case "$rid" in
    win-*)   preset="Windows Desktop"; exe_name="Eden.Viewer.exe" ;;
    linux-*) preset="Linux/X11";        exe_name="Eden.Viewer" ;;
    osx-*)   preset="macOS";            exe_name="Eden.Viewer.app" ;;
    *)       preset="Windows Desktop";  exe_name="Eden.Viewer.exe" ;;
esac

mkdir -p "$viewer_out"
viewer_exe="$viewer_out/$exe_name"

echo ""
echo "exporting Eden.Viewer ($preset) via $godot -> $viewer_exe"
"$godot" --headless --path "$viewer_project" --export-release "$preset" "$viewer_exe"
rc=$?

if [[ $rc -ne 0 ]]; then
    echo ""
    echo "viewer export failed (exit $rc). common causes:"
    echo "  - export templates for Godot 4.6.1 not installed"
    echo "    (Godot editor -> Editor -> Manage Export Templates)"
    echo "  - export_presets.cfg is stale - open the project in Godot,"
    echo "    Project -> Export, save, commit export_presets.cfg"
    exit $rc
fi

echo ""
echo "viewer binary: $viewer_exe"
