#!/usr/bin/env bash
# publish.sh — Upload Breakdown to Steam Workshop via SteamCMD.
#
# Prerequisites:
#   - steamcmd installed (sudo apt-get install steamcmd)
#   - STEAM_USERNAME set in .env (or exported in the shell)
#   - dist/Breakdown/ already staged by deploy.sh --release
#
# Usage:
#   ./deploy.sh --release           # build & stage first
#   ./publish.sh                    # upload with default change note
#   ./publish.sh "Fix: route display" # upload with a custom change note

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# ── Load .env if present ─────────────────────────────────────────────────────
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

# ── Configuration ─────────────────────────────────────────────────────────────
APP_ID="255710"
ITEM_ID="${WORKSHOP_ITEM_ID:-}"   # set WORKSHOP_ITEM_ID in .env once published
MOD_NAME="Breakdown"
DIST="$SCRIPT_DIR/dist/$MOD_NAME"
WORKSHOP_DIR="$SCRIPT_DIR/Workshop"
VDF="$WORKSHOP_DIR/item.vdf"
CHANGE_NOTE="${1:-Update}"
STEAM_USERNAME="${STEAM_USERNAME:-}"

# ── Preflight checks ──────────────────────────────────────────────────────────
if ! command -v steamcmd &>/dev/null; then
    echo "ERROR: steamcmd not found."
    echo "  Install it with: sudo apt-get install steamcmd"
    exit 1
fi

if [[ -z "$STEAM_USERNAME" ]]; then
    echo "ERROR: STEAM_USERNAME is not set."
    echo "  Add it to your .env file:  STEAM_USERNAME=your_steam_username"
    exit 1
fi

if [[ -z "$ITEM_ID" ]]; then
    echo "ERROR: WORKSHOP_ITEM_ID is not set."
    echo "  Add it to your .env file after the first Workshop publish."
    exit 1
fi

if [[ ! -f "$DIST/$MOD_NAME.dll" ]]; then
    echo "ERROR: dist/$MOD_NAME/$MOD_NAME.dll not found."
    echo "  Run ./deploy.sh --release first to build and stage the mod."
    exit 1
fi

mkdir -p "$WORKSHOP_DIR"

# ── Generate VDF ─────────────────────────────────────────────────────────────
cat > "$VDF" <<EOF
"workshopitem"
{
    "appid"           "$APP_ID"
    "publishedfileid" "$ITEM_ID"
    "contentfolder"   "$DIST"
    "previewfile"     "$WORKSHOP_DIR/PreviewImage.png"
    "visibility"      "0"
    "title"           "Breakdown"
    "changenote"      "$CHANGE_NOTE"
}
EOF

echo "Generated: $VDF"
echo ""
echo "Uploading '$MOD_NAME' to Workshop (item $ITEM_ID, app $APP_ID)..."
echo "Change note: $CHANGE_NOTE"
echo ""
echo "SteamCMD will prompt for your password and Steam Guard code."
echo "──────────────────────────────────────────────────────────────"

steamcmd \
    +login "$STEAM_USERNAME" \
    +workshop_build_item "$VDF" \
    +quit

echo ""
echo "Done. Check the Workshop page:"
echo "  https://steamcommunity.com/sharedfiles/filedetails/?id=$ITEM_ID"
