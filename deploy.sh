#!/usr/bin/env bash
# deploy.sh — Build Breakdown, stage files, and copy to the game's Mods folder.
#
# Configuration is read from .env in the repo root (copy .env.example to .env).
#
# Usage:
#   ./deploy.sh           → Debug build
#   ./deploy.sh --release → Release build

set -euo pipefail

# ── Load .env if present ────────────────────────────────────────────────────────
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ -f "$SCRIPT_DIR/.env" ]]; then
    while IFS='=' read -r _key _value; do
        [[ "$_key" =~ ^[[:space:]]*# ]] && continue
        [[ -z "$_key" ]]               && continue
        _value="${_value%\"}"  ; _value="${_value#\"}"
        _value="${_value%\'}"  ; _value="${_value#\'}"
        export "$_key=$_value"
    done < "$SCRIPT_DIR/.env"
    unset _key _value
fi

# ── Configuration ───────────────────────────────────────────────────────────────
CONFIGURATION="Debug"
if [[ "${1:-}" == "--release" ]]; then
    CONFIGURATION="Release"
fi

MOD_NAME="BreakdownRevisited"
BUILD_OUT="$SCRIPT_DIR/bin/$CONFIGURATION"
DIST="$SCRIPT_DIR/dist/$MOD_NAME"

# Mount points — defaults match mount-cities.sh; override via .env if needed.
GAME_MOUNT="${CITIES_GAME_MOUNT:-/mnt/cities_skylines}"
DATA_MOUNT="${CITIES_DATA_MOUNT:-/mnt/cities_skylines_data}"
MODS_DIR="$DATA_MOUNT/Addons/Mods/$MOD_NAME"
LOG_FILE="$GAME_MOUNT/Cities_Data/output_log.txt"

# ── Build ────────────────────────────────────────────────────────────────────────
echo "Building ($CONFIGURATION)..."
cd "$SCRIPT_DIR"
xbuild Breakdown.sln /p:Configuration="$CONFIGURATION" /nologo /verbosity:quiet
echo "Build succeeded."

# ── Stage to dist/ ───────────────────────────────────────────────────────────────
rm -rf "$DIST"
mkdir -p "$DIST"

cp "$BUILD_OUT/$MOD_NAME.dll" "$DIST/$MOD_NAME.dll"

echo ""
echo "Staged to: $DIST"
ls -lh "$DIST"

# ── Deploy to game (if mounted) ───────────────────────────────────────────────────
if mountpoint -q "$DATA_MOUNT"; then
    echo ""
    echo "Game mount detected — copying to $MODS_DIR ..."
    mkdir -p "$MODS_DIR"
    cp "$DIST/$MOD_NAME.dll" "$MODS_DIR/"
    echo "Done. Files in game Mods folder:"
    ls -lh "$MODS_DIR"
else
    echo ""
    echo "Game mount not detected — skipping in-game deploy."
    echo "Run ./mount-cities.sh first, then re-run this script."
fi

# ── Show log tail (if mounted) ───────────────────────────────────────────────────
echo ""
if [[ -f "$LOG_FILE" ]]; then
    echo "════════════════════════  output_log.txt (last 60 lines)  ════════════════════════"
    tail -n 60 "$LOG_FILE"
    echo "══════════════════════════════════════════════════════════════════════════════════"
else
    echo "Log not found at $LOG_FILE"
    echo "Run ./mount-cities.sh to mount the game share."
fi
