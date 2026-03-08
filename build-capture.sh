#!/bin/bash

set -euo pipefail

SCRIPT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_ROOT"
PROJECT_PATH="$PROJECT_ROOT/tools/capture-cli/capture-cli.csproj"
OUTPUT_DIR="$PROJECT_ROOT/.flow/bin"
PUBLISH_DIR="$OUTPUT_DIR/.capture-publish"
CONFIGURATION="Release"

usage() {
    cat <<'EOF'
Usage: ./build-capture.sh [--configuration <Debug|Release>]

Options:
  --configuration <cfg>  Build configuration. Default: Release
  -h, --help             Show this help
EOF
}

require_cmd() {
    if ! command -v "$1" >/dev/null 2>&1; then
        echo "[ERROR] Required command not found: $1" >&2
        exit 1
    fi
}

file_size_mb() {
    local bytes
    if stat -f%z "$1" >/dev/null 2>&1; then
        bytes="$(stat -f%z "$1")"
    else
        bytes="$(stat -c%s "$1")"
    fi
    python3 - "$bytes" <<'PY'
import sys
print(f"{int(sys.argv[1]) / (1024 * 1024):.2f}")
PY
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --configuration)
            shift
            if [[ $# -eq 0 ]]; then
                echo "[ERROR] --configuration requires a value" >&2
                exit 1
            fi
            CONFIGURATION="$1"
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

case "$CONFIGURATION" in
    Debug|Release)
        ;;
    *)
        echo "[ERROR] Configuration must be Debug or Release" >&2
        exit 1
        ;;
esac

require_cmd dotnet
require_cmd python3

if [[ ! -f "$PROJECT_PATH" ]]; then
    echo "[ERROR] Project not found: $PROJECT_PATH" >&2
    exit 1
fi

echo "======================================="
echo "  Capture CLI Build"
echo "======================================="
echo
echo "Configuration: $CONFIGURATION"
echo "Output: $OUTPUT_DIR"
echo
echo "[INFO] tools/capture-cli targets Windows (win-x64). Building on macOS/Linux may require a compatible cross-compilation toolchain."

mkdir -p "$OUTPUT_DIR"
rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR"

publish_args=(
    publish "$PROJECT_PATH"
    -c "$CONFIGURATION"
    -r win-x64
    --self-contained
    -p:PublishSingleFile=true
    -p:EnableCompressionInSingleFile=true
    -p:DebugType=None
    -p:DebugSymbols=false
    -o "$PUBLISH_DIR"
)

if [[ "$CONFIGURATION" == "Release" ]]; then
    publish_args+=(
        -p:DebugType=none
        -p:DebugSymbols=false
    )
fi

echo "Building capture-cli..."

if output="$(dotnet "${publish_args[@]}" 2>&1)"; then
    publish_exe="$PUBLISH_DIR/capture.exe"
    output_file="$OUTPUT_DIR/capture.exe"

    if [[ -f "$publish_exe" ]]; then
        rm -f "$OUTPUT_DIR"/capture.*
        cp "$publish_exe" "$output_file"
    fi

    if [[ -f "$output_file" ]]; then
        echo
        echo "======================================="
        echo "  Build Summary"
        echo "======================================="
        echo "  [OK] Success ($(file_size_mb "$output_file") MB)"
        echo "  Path: $output_file"
        echo
        echo "Usage examples:"
        echo "  capture list-windows"
        echo '  capture window --name "메모장" --output screenshot.png'
        echo "  capture monitor --index 0 --output monitor.png"
    else
        echo "[ERROR] Output file not found: $output_file" >&2
        exit 1
    fi
else
    echo "[ERROR] Build failed"
    printf '%s\n' "$output"
    exit 1
fi

rm -rf "$PUBLISH_DIR"