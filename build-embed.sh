#!/bin/bash

set -euo pipefail

SCRIPT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_ROOT"
PROJECT_PATH="$PROJECT_ROOT/tools/embed/embed.csproj"

CONFIGURATION="Release"
RUNTIMES=()

usage() {
    cat <<'EOF'
Usage: ./build-embed.sh [--configuration <Debug|Release>] [--runtime <rid>]...

Options:
  --configuration <cfg>  Build configuration. Default: Release
  --runtime <rid>        Target runtime. Repeatable. Default: win-x64
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

validate_runtime() {
    case "$1" in
        win-x64|linux-x64|osx-x64)
            ;;
        *)
            echo "[ERROR] Unsupported runtime: $1" >&2
            exit 1
            ;;
    esac
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
        --runtime)
            shift
            if [[ $# -eq 0 ]]; then
                echo "[ERROR] --runtime requires a value" >&2
                exit 1
            fi
            validate_runtime "$1"
            RUNTIMES+=("$1")
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

if [[ ${#RUNTIMES[@]} -eq 0 ]]; then
    RUNTIMES=("win-x64")
fi

echo "======================================="
echo "  Embed CLI Build"
echo "======================================="
echo
echo "Configuration: $CONFIGURATION"
echo "Targets: ${RUNTIMES[*]}"
echo
echo "[INFO] tools/embed targets net8.0-windows and DirectML. Non-Windows builds may fail depending on the local toolchain."

SUCCESS_COUNT=0
TOTAL_COUNT=${#RUNTIMES[@]}

for rid in "${RUNTIMES[@]}"; do
    echo "Building for $rid..."

    output_root="$PROJECT_ROOT/.flow/rag/bin"
    if [[ ${#RUNTIMES[@]} -gt 1 ]]; then
        output_dir="$output_root/$rid"
    else
        output_dir="$output_root"
    fi
    mkdir -p "$output_dir"

    publish_args=(
        publish "$PROJECT_PATH"
        -c "$CONFIGURATION"
        -r "$rid"
        --self-contained
        -p:PublishAot=true
        -o "$output_dir"
    )

    if [[ "$CONFIGURATION" == "Release" ]]; then
        publish_args+=(
            -p:StripSymbols=true
            -p:OptimizationPreference=Speed
        )
    fi

    if output="$(dotnet "${publish_args[@]}" 2>&1)"; then
        if [[ "$rid" == "win-x64" ]]; then
            output_path="$output_dir/embed.exe"
        else
            output_path="$output_dir/embed"
        fi

        if [[ -f "$output_path" ]]; then
            echo "  [OK] Success ($(file_size_mb "$output_path") MB)"
            echo "  Path: $output_path"
            SUCCESS_COUNT=$((SUCCESS_COUNT + 1))
        else
            echo "  [ERROR] Output file not found: $output_path"
        fi
    else
        echo "  [ERROR] Build failed for $rid"
        printf '%s\n' "$output"
    fi

    echo
done

echo "======================================="
echo "  Build Summary"
echo "======================================="

if [[ "$SUCCESS_COUNT" -eq "$TOTAL_COUNT" ]]; then
    echo "  [OK] All builds successful ($SUCCESS_COUNT/$TOTAL_COUNT)"
else
    echo "  [WARN] Some builds failed ($SUCCESS_COUNT/$TOTAL_COUNT successful)"
    exit 1
fi