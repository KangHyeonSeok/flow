#!/bin/bash
# Flow Prompt 설치 스크립트 (macOS/Linux)
# 사용법: curl -fsSL https://raw.githubusercontent.com/OWNER/REPO/main/install.sh | bash

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
echo -e "${BLUE}  Flow Prompt 설치${NC}"
echo -e "${BLUE}═══════════════════════════════════════${NC}"
echo ""

# 1. 최신 릴리스 정보 조회
step "최신 릴리스 확인 중..."
API_URL="https://api.github.com/repos/$REPO/releases/latest"

if command -v curl &> /dev/null; then
    RELEASE_JSON=$(curl -fsSL -H "User-Agent: bash" "$API_URL")
elif command -v wget &> /dev/null; then
    RELEASE_JSON=$(wget -qO- --header="User-Agent: bash" "$API_URL")
else
    warn "curl 또는 wget이 필요합니다."
    exit 1
fi

VERSION=$(echo "$RELEASE_JSON" | grep -o '"tag_name": *"[^"]*"' | head -1 | sed 's/.*"v\?\([^"]*\)".*/\1/')
DOWNLOAD_URL=$(echo "$RELEASE_JSON" | grep -o '"browser_download_url": *"[^"]*\.zip"' | head -1 | sed 's/.*"\(http[^"]*\)".*/\1/')

if [ -z "$DOWNLOAD_URL" ]; then
    warn "zip 파일을 찾을 수 없습니다."
    echo "  직접 다운로드: https://github.com/$REPO/releases"
    exit 1
fi

success "버전 $VERSION 발견"

# 2. 기존 .flow 백업
FLOW_DIR=".flow"
BACKUP_DIR=".flow.bak"

if [ -d "$FLOW_DIR" ]; then
    step "기존 .flow 백업 중..."
    rm -rf "$BACKUP_DIR"
    mv "$FLOW_DIR" "$BACKUP_DIR"
    success "백업 완료: .flow.bak"
fi

# 3. zip 다운로드
step "다운로드 중..."
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

# 4. 압축 해제
step "압축 해제 중..."

if command -v unzip &> /dev/null; then
    unzip -q "$TEMP_ZIP" -d "$TEMP_DIR"
else
    warn "unzip이 필요합니다."
    # 백업 복원
    if [ -d "$BACKUP_DIR" ]; then
        mv "$BACKUP_DIR" "$FLOW_DIR"
        echo "  백업에서 복원됨"
    fi
    exit 1
fi

# .flow 폴더 복사
if [ -d "$TEMP_DIR/.flow" ]; then
    cp -r "$TEMP_DIR/.flow" "$FLOW_DIR"
else
    warn ".flow 폴더를 찾을 수 없습니다."
    # 백업 복원
    if [ -d "$BACKUP_DIR" ]; then
        mv "$BACKUP_DIR" "$FLOW_DIR"
        echo "  백업에서 복원됨"
    fi
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
    fi
fi

success "설치 완료"

# 5. 설치된 버전 확인
INSTALLED_VERSION=""
if [ -f "$FLOW_DIR/version.txt" ]; then
    INSTALLED_VERSION=$(cat "$FLOW_DIR/version.txt" | tr -d '[:space:]')
fi

echo ""
echo -e "${GREEN}═══════════════════════════════════════${NC}"
success "Flow Prompt v$INSTALLED_VERSION installed"
echo -e "${GREEN}═══════════════════════════════════════${NC}"
echo ""
echo "  사용법: Copilot Chat에서 /flow 입력"
echo ""
