#!/bin/bash

set -euo pipefail

SCRIPT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_ROOT"
EXT_DIR="$PROJECT_ROOT/tools/flow-ext"

VERSION=""
OUTPUT_DIR=""

usage() {
    cat <<'EOF'
Usage: ./build-flow-ext.sh [--version <version>] [--output-dir <dir>]

Options:
  --version <ver>     Package version. Defaults to VERSION file.
  --output-dir <dir>  Output directory for .vsix. Defaults to ./dist
  -h, --help          Show this help
EOF
}

require_cmd() {
    if ! command -v "$1" >/dev/null 2>&1; then
        echo "[ERROR] Required command not found: $1" >&2
        exit 1
    fi
}

resolve_vscode_cli() {
    local candidates=()

    if command -v code >/dev/null 2>&1; then
        command -v code
        return 0
    fi

    candidates=(
        "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code"
        "/Applications/Visual Studio Code Insiders.app/Contents/Resources/app/bin/code"
        "$HOME/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code"
        "$HOME/Applications/Visual Studio Code Insiders.app/Contents/Resources/app/bin/code"
    )

    for candidate in "${candidates[@]}"; do
        if [[ -x "$candidate" ]]; then
            echo "$candidate"
            return 0
        fi
    done

    return 1
}

remove_legacy_flow_extensions() {
    local vscode_cli="$1"
    local legacy_extension_ids=(
        "flow-team.spec-graph"
    )

    for extension_id in "${legacy_extension_ids[@]}"; do
        echo "[INFO] attempting to remove legacy extension: $extension_id"
        "$vscode_cli" --uninstall-extension "$extension_id" || true
    done
}

write_reload_signal() {
    local version_text="$1"
    local signal_dir="$PROJECT_ROOT/.flow"
    local signal_path="$signal_dir/flow-ext.reload.signal"

    mkdir -p "$signal_dir"
    python3 - "$signal_path" "$version_text" <<'PY'
import json
import sys
from datetime import datetime, timezone

path, version = sys.argv[1], sys.argv[2]
payload = {
    'requestedAt': datetime.now(timezone.utc).isoformat(),
    'version': version,
    'source': 'build-flow-ext.sh',
}
with open(path, 'w', encoding='utf-8') as f:
    json.dump(payload, f, ensure_ascii=False, indent=2)
    f.write('\n')
PY
    echo "[INFO] auto reload signal written: $signal_path"
}

update_package_version() {
    local package_json="$1"
    local version="$2"
    python3 - "$package_json" "$version" <<'PY'
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
        --version)
            shift
            if [[ $# -eq 0 ]]; then
                echo "[ERROR] --version requires a value" >&2
                exit 1
            fi
            VERSION="$1"
            ;;
        --output-dir)
            shift
            if [[ $# -eq 0 ]]; then
                echo "[ERROR] --output-dir requires a value" >&2
                exit 1
            fi
            OUTPUT_DIR="$1"
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

require_cmd node
require_cmd npm
require_cmd npx
require_cmd python3

if [[ -z "$VERSION" ]]; then
    if [[ -f "$PROJECT_ROOT/VERSION" ]]; then
        VERSION="$(tr -d '[:space:]' < "$PROJECT_ROOT/VERSION")"
        echo "[INFO] version from VERSION: $VERSION"
    else
        VERSION="0.1.0"
        echo "[WARN] VERSION file not found, using default: $VERSION"
    fi
fi

if [[ -z "$OUTPUT_DIR" ]]; then
    OUTPUT_DIR="$PROJECT_ROOT/dist"
fi
mkdir -p "$OUTPUT_DIR"

echo "=== Flow Extension Build ==="
echo "[INFO] Node: $(node --version), npm: $(npm --version)"

pushd "$EXT_DIR" >/dev/null
trap 'popd >/dev/null' EXIT

echo "[1/5] package.json version update: $VERSION"
update_package_version "$EXT_DIR/package.json" "$VERSION"

echo "[2/5] npm install..."
npm install --ignore-scripts

echo "[3/5] build..."
npm run build

if [[ ! -f "$EXT_DIR/LICENSE" && -f "$PROJECT_ROOT/LICENSE" ]]; then
    cp "$PROJECT_ROOT/LICENSE" "$EXT_DIR/LICENSE"
fi

echo "[4/5] packaging .vsix..."
npx vsce package --no-dependencies --allow-missing-repository

latest_vsix="$(ls -t ./*.vsix 2>/dev/null | head -n 1 || true)"
if [[ -z "$latest_vsix" ]]; then
    echo "[ERROR] .vsix file not found after packaging" >&2
    exit 1
fi

dest_path="$OUTPUT_DIR/flow-ext-$VERSION.vsix"
mv -f "$latest_vsix" "$dest_path"

echo
echo "=== Build Complete ==="
echo "  .vsix: $dest_path"

if vscode_cli="$(resolve_vscode_cli 2>/dev/null)"; then
    echo "[5/5] installing extension..."
    echo "[INFO] VS Code CLI: $vscode_cli"
    remove_legacy_flow_extensions "$vscode_cli"
    "$vscode_cli" --install-extension "$dest_path" --force
    echo "[INFO] install complete"
else
    echo "[WARN] VS Code CLI(code) not found. Skipping install step."
fi

write_reload_signal "$VERSION"

trap - EXIT
popd >/dev/null