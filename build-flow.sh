#!/bin/bash

set -euo pipefail

SCRIPT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_ROOT"

CONFIG="Debug"
RUNTIME=""
SKIP_MODULES=0

usage() {
    cat <<'EOF'
Usage: ./build-flow.sh [--release] [--runtime <rid>] [--skip-modules]

Options:
  --release         Build in Release mode
  --runtime <rid>   Runtime identifier (win-x64, linux-x64, osx-x64, osx-arm64)
  --skip-modules    Skip build module packaging
  -h, --help        Show this help
EOF
}

require_cmd() {
    if ! command -v "$1" >/dev/null 2>&1; then
        echo "[ERROR] Required command not found: $1" >&2
        exit 1
    fi
}

file_size_bytes() {
    if stat -f%z "$1" >/dev/null 2>&1; then
        stat -f%z "$1"
    else
        stat -c%s "$1"
    fi
}

detect_default_runtime() {
    local uname_s uname_m
    uname_s="$(uname -s 2>/dev/null || echo unknown)"
    uname_m="$(uname -m 2>/dev/null || echo unknown)"

    case "$uname_s" in
        Darwin)
            if [[ "$uname_m" == "arm64" ]]; then
                echo "osx-arm64"
            else
                echo "osx-x64"
            fi
            ;;
        Linux)
            echo "linux-x64"
            ;;
        *)
            echo "win-x64"
            ;;
    esac
}

update_manifest_version() {
    local manifest_path="$1"
    local version="$2"
    python3 - "$manifest_path" "$version" <<'PY'
import json
import sys

path, version = sys.argv[1], sys.argv[2]
with open(path, 'r', encoding='utf-8-sig') as f:
    data = json.load(f)
data['version'] = version
with open(path, 'w', encoding='utf-8') as f:
    json.dump(data, f, ensure_ascii=False, indent=2)
    f.write('\n')
PY
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --release)
            CONFIG="Release"
            ;;
        --runtime)
            shift
            if [[ $# -eq 0 ]]; then
                echo "[ERROR] --runtime requires a value" >&2
                exit 1
            fi
            RUNTIME="$1"
            ;;
        --skip-modules)
            SKIP_MODULES=1
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "[ERROR] Unknown argument: $1" >&2
            usage >&2
            exit 1
            ;;
    esac
    shift
done

require_cmd dotnet
require_cmd zip
require_cmd python3

if [[ -z "$RUNTIME" ]]; then
    RUNTIME="$(detect_default_runtime)"
fi

PROJECT_DIR="$PROJECT_ROOT/tools/flow-cli"
OUTPUT_DIR="$PROJECT_ROOT/.flow/bin"

mkdir -p "$OUTPUT_DIR"

echo "======================================="
echo " Flow CLI Build ($CONFIG, $RUNTIME)"
echo "======================================="

dotnet publish "$PROJECT_DIR" -c "$CONFIG" -r "$RUNTIME" --self-contained -o "$OUTPUT_DIR"

if [[ "$RUNTIME" == win-* ]]; then
    FLOW_OUTPUT="$OUTPUT_DIR/flow.exe"
elif [[ "$RUNTIME" == linux-* || "$RUNTIME" == osx-* ]]; then
    FLOW_OUTPUT="$OUTPUT_DIR/flow"
else
    FLOW_OUTPUT="$OUTPUT_DIR/flow"
fi

if [[ ! -f "$FLOW_OUTPUT" ]]; then
    echo "[ERROR] Build succeeded but output was not found: $FLOW_OUTPUT" >&2
    exit 1
fi

chmod +x "$FLOW_OUTPUT" 2>/dev/null || true

case "$RUNTIME" in
    osx-arm64)
        cp "$FLOW_OUTPUT" "$OUTPUT_DIR/flow-osx-arm64"
        chmod +x "$OUTPUT_DIR/flow-osx-arm64"
        ;;
    osx-x64)
        cp "$FLOW_OUTPUT" "$OUTPUT_DIR/flow-osx-x64"
        chmod +x "$OUTPUT_DIR/flow-osx-x64"
        ;;
    linux-*)
        cp "$FLOW_OUTPUT" "$OUTPUT_DIR/flow-linux"
        chmod +x "$OUTPUT_DIR/flow-linux"
        ;;
esac

echo "[OK] Build successful: $FLOW_OUTPUT"

if [[ "$SKIP_MODULES" -eq 1 ]]; then
    exit 0
fi

BUILD_MODULES_DIR="$PROJECT_ROOT/tools/build"
DIST_DIR="$PROJECT_ROOT/dist"
VERSION_FILE="$PROJECT_ROOT/VERSION"

mkdir -p "$DIST_DIR"

if [[ -f "$VERSION_FILE" ]]; then
    VERSION="$(tr -d '[:space:]' < "$VERSION_FILE")"
else
    VERSION="0.0.0"
fi

echo
echo "======================================="
echo " Build Module Packaging"
echo "======================================="

MODULE_COUNT=0
if [[ -d "$BUILD_MODULES_DIR" ]]; then
    shopt -s nullglob
    for module_dir in "$BUILD_MODULES_DIR"/*; do
        [[ -d "$module_dir" ]] || continue

        manifest_path="$module_dir/manifest.json"
        module_name="$(basename "$module_dir")"

        if [[ ! -f "$manifest_path" ]]; then
            echo "  [WARN] $module_name: manifest.json not found, skipping"
            continue
        fi

        update_manifest_version "$manifest_path" "$VERSION"

        zip_name="build-module-$module_name.zip"
        zip_path="$DIST_DIR/$zip_name"
        rm -f "$zip_path"
        (
            cd "$module_dir"
            zip -qr "$zip_path" .
        )

        zip_size_kb=$(( $(file_size_bytes "$zip_path") / 1024 ))
        echo "  [OK] $zip_name (${zip_size_kb} KB)"
        MODULE_COUNT=$((MODULE_COUNT + 1))
    done
fi

if [[ "$MODULE_COUNT" -eq 0 ]]; then
    echo "  [INFO] No build modules packaged"
else
    echo "[OK] $MODULE_COUNT build modules packaged -> $DIST_DIR"
fi