#!/bin/bash
# Flow Prompt 업데이트 스크립트 (macOS/Linux)
# 사용법: curl -fsSL https://raw.githubusercontent.com/OWNER/REPO/main/update.sh | bash

set -e

REPO="${FLOW_REPO:-KangHyeonSeok/flow}"

# 색상 정의
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

step() {
    echo -e "  ${CYAN}→ $1${NC}"
}

success() {
    echo -e "  ${GREEN}✅ $1${NC}"
}

warn() {
    echo -e "  ${YELLOW}⚠️ $1${NC}"
}

echo ""
echo -e "${BLUE}═══════════════════════════════════════${NC}"
echo -e "${BLUE}  Flow Prompt 업데이트 확인${NC}"
echo -e "${BLUE}═══════════════════════════════════════${NC}"
echo ""

# 1. 현재 버전 확인
FLOW_DIR=".flow"
VERSION_FILE="$FLOW_DIR/version.txt"

if [ ! -f "$VERSION_FILE" ]; then
    warn "Flow가 설치되어 있지 않습니다."
    echo "  설치하려면: curl -fsSL https://raw.githubusercontent.com/$REPO/main/install.sh | bash"
    exit 1
fi

CURRENT_VERSION=$(cat "$VERSION_FILE" | tr -d '[:space:]')
step "현재 버전: $CURRENT_VERSION"

# 2. 최신 릴리스 정보 조회
step "최신 버전 확인 중..."
API_URL="https://api.github.com/repos/$REPO/releases/latest"

if command -v curl &> /dev/null; then
    RELEASE_JSON=$(curl -fsSL -H "User-Agent: bash" "$API_URL")
elif command -v wget &> /dev/null; then
    RELEASE_JSON=$(wget -qO- --header="User-Agent: bash" "$API_URL")
else
    warn "curl 또는 wget이 필요합니다."
    exit 1
fi

LATEST_VERSION=$(echo "$RELEASE_JSON" | grep -o '"tag_name": *"[^"]*"' | head -1 | sed 's/.*"v\?\([^"]*\)".*/\1/')
step "최신 버전: $LATEST_VERSION"

# 3. 버전 비교
if [ "$CURRENT_VERSION" = "$LATEST_VERSION" ]; then
    echo ""
    echo -e "${GREEN}═══════════════════════════════════════${NC}"
    success "Already up to date (v$CURRENT_VERSION)"
    echo -e "${GREEN}═══════════════════════════════════════${NC}"
    echo ""
    exit 0
fi

echo ""
echo -e "  ${YELLOW}새 버전 발견: $CURRENT_VERSION → $LATEST_VERSION${NC}"
echo ""

# 4. 설치 로직 실행
step "업데이트 중..."

DOWNLOAD_URL=$(echo "$RELEASE_JSON" | grep -o '"browser_download_url": *"[^"]*\.zip"' | head -1 | sed 's/.*"\(http[^"]*\)".*/\1/')

if [ -z "$DOWNLOAD_URL" ]; then
    warn "zip 파일을 찾을 수 없습니다."
    exit 1
fi

# 기존 .flow 제거 (백업 없이)
if [ -d "$FLOW_DIR" ]; then
    step "기존 .flow 제거 중..."
    rm -rf "$FLOW_DIR"
fi

# zip 다운로드
TEMP_ZIP=$(mktemp /tmp/flow-prompts-XXXXXX.zip)
TEMP_DIR=$(mktemp -d /tmp/flow-extract-XXXXXX)

cleanup() {
    rm -f "$TEMP_ZIP"
    rm -rf "$TEMP_DIR"
}
trap cleanup EXIT

if command -v curl &> /dev/null; then
    curl -fsSL -o "$TEMP_ZIP" "$DOWNLOAD_URL"
elif command -v wget &> /dev/null; then
    wget -qO "$TEMP_ZIP" "$DOWNLOAD_URL"
fi

success "다운로드 완료"

# 압축 해제
if command -v unzip &> /dev/null; then
    unzip -q "$TEMP_ZIP" -d "$TEMP_DIR"
else
    warn "unzip이 필요합니다."
    exit 1
fi

if [ -d "$TEMP_DIR/.flow" ]; then
    cp -r "$TEMP_DIR/.flow" "$FLOW_DIR"
else
    warn ".flow 폴더를 찾을 수 없습니다."
    exit 1
fi

# .claude 폴더 복사 (있으면)
if [ -d "$TEMP_DIR/.claude" ]; then
    cp -r "$TEMP_DIR/.claude" ".claude"
fi

# .github/prompts 폴더 복사 (있으면)
if [ -d "$TEMP_DIR/prompts" ]; then
    if find "$TEMP_DIR/prompts" -mindepth 1 -maxdepth 1 -print -quit | read -r; then
        mkdir -p ".github/prompts"
        cp -r "$TEMP_DIR/prompts/." ".github/prompts/"
        
        # .claude/commands 폴더에도 복사
        mkdir -p ".claude/commands"
        cp -r "$TEMP_DIR/prompts/." ".claude/commands/"
    fi
fi

# 필수 디렉토리 사전 생성
mkdir -p ".github/prompts"
mkdir -p ".claude/commands"
mkdir -p "docs"
mkdir -p "docs/flow"
mkdir -p "docs/flow/backlogs"
mkdir -p "docs/flow/implements"
mkdir -p "docs/flow/meta"

success "업데이트 완료"

# 설치된 버전 확인
INSTALLED_VERSION=""
if [ -f "$VERSION_FILE" ]; then
    INSTALLED_VERSION=$(cat "$VERSION_FILE" | tr -d '[:space:]')
fi

echo ""
echo -e "${GREEN}═══════════════════════════════════════${NC}"
success "Flow Prompt v$INSTALLED_VERSION installed"
echo "  업데이트됨: $CURRENT_VERSION → $INSTALLED_VERSION"
echo -e "${GREEN}═══════════════════════════════════════${NC}"
echo ""
