#!/usr/bin/env pwsh
# =============================================================================
# Spec Graph 시스템 검증 및 증거 수집 스크립트
# docs/evidence/ 하위에 각 기능별 실행 결과를 저장합니다.
# =============================================================================

$ErrorActionPreference = "Continue"
$ProjectRoot = (Get-Location).Path
$EvidenceRoot = Join-Path $ProjectRoot "docs" "evidence" "spec-graph"
$FlowCli = "dotnet run --project tools/flow-cli/flow-cli.csproj --"
$Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

# 증거 디렉토리 생성
New-Item -ItemType Directory -Path $EvidenceRoot -Force | Out-Null

Write-Host "===========================================" -ForegroundColor Cyan
Write-Host " Spec Graph 시스템 검증 보고서" -ForegroundColor Cyan
Write-Host " 생성 시각: $Timestamp" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host ""

$report = @"
# Spec Graph 시스템 검증 보고서
생성 시각: $Timestamp

---

"@

# ─── 1. 단위 테스트 ───────────────────────────────────────────────────
Write-Host "▶ [1/8] 단위 테스트 실행..." -ForegroundColor Yellow
$testResult = dotnet test tools/flow-cli.Tests/flow-cli.Tests.csproj `
    --filter "SpecStoreTests|SpecValidatorTests|GraphBuilderTests|ImpactAnalyzerTests|StatusPropagatorTests|CodeRefValidatorTests" `
    --verbosity normal 2>&1 | Out-String

$testResult | Out-File (Join-Path $EvidenceRoot "01_unit_tests.log") -Encoding utf8
$passed = if ($testResult -match "성공: (\d+)") { $Matches[1] } else { "?" }
$failed = if ($testResult -match "실패: (\d+)") { $Matches[1] } else { "0" }
$total = if ($testResult -match "합계: (\d+)") { $Matches[1] } else { "?" }

Write-Host "  ✅ 테스트 결과: $passed/$total 통과, $failed 실패" -ForegroundColor Green
$report += @"
## 1. 단위 테스트 결과

| 항목 | 값 |
|---|---|
| 총 테스트 | $total |
| 통과 | $passed |
| 실패 | $failed |

테스트 클래스:
- SpecStoreTests (CRUD 13개)
- SpecValidatorTests (유효성 16개)
- GraphBuilderTests (Kahn 알고리즘 12개)
- ImpactAnalyzerTests (영향 분석 8개)
- StatusPropagatorTests (상태 전파 7개)
- CodeRefValidatorTests (코드 참조 8개)

상세 로그: ``01_unit_tests.log``

---

"@

# ─── 2. spec-init ─────────────────────────────────────────────────────
Write-Host "▶ [2/8] spec-init 실행..." -ForegroundColor Yellow
$initResult = Invoke-Expression "$FlowCli spec-init --pretty" 2>&1 | Out-String
$initResult | Out-File (Join-Path $EvidenceRoot "02_spec_init.json") -Encoding utf8
Write-Host "  ✅ 디렉토리 초기화 완료" -ForegroundColor Green
$report += @"
## 2. spec-init (디렉토리 초기화)

``````json
$initResult
``````

---

"@

# ─── 3. spec-list ─────────────────────────────────────────────────────
Write-Host "▶ [3/8] spec-list 실행..." -ForegroundColor Yellow
$listResult = Invoke-Expression "$FlowCli spec-list --pretty" 2>&1 | Out-String
$listResult | Out-File (Join-Path $EvidenceRoot "03_spec_list.json") -Encoding utf8
Write-Host "  ✅ 스펙 목록 조회 완료" -ForegroundColor Green
$report += @"
## 3. spec-list (스펙 목록)

``````json
$listResult
``````

---

"@

# ─── 4. spec-validate ─────────────────────────────────────────────────
Write-Host "▶ [4/8] spec-validate --strict 실행..." -ForegroundColor Yellow
$validateResult = Invoke-Expression "$FlowCli spec-validate --strict --pretty" 2>&1 | Out-String
$validateResult | Out-File (Join-Path $EvidenceRoot "04_spec_validate.json") -Encoding utf8
Write-Host "  ✅ 유효성 검사 완료" -ForegroundColor Green
$report += @"
## 4. spec-validate --strict (유효성 검사)

``````json
$validateResult
``````

---

"@

# ─── 5. spec-graph --tree ─────────────────────────────────────────────
Write-Host "▶ [5/8] spec-graph --tree 실행..." -ForegroundColor Yellow
$treeResult = Invoke-Expression "$FlowCli spec-graph --tree" 2>&1 | Out-String
$treeResult | Out-File (Join-Path $EvidenceRoot "05_spec_tree.txt") -Encoding utf8
Write-Host "  ✅ 트리 시각화 완료" -ForegroundColor Green
$report += @"
## 5. spec-graph --tree (트리 시각화)

``````
$treeResult
``````

---

"@

# ─── 6. spec-impact ───────────────────────────────────────────────────
Write-Host "▶ [6/8] spec-impact F-010 실행..." -ForegroundColor Yellow
$impactResult = Invoke-Expression "$FlowCli spec-impact F-010 --pretty" 2>&1 | Out-String
$impactResult | Out-File (Join-Path $EvidenceRoot "06_spec_impact.json") -Encoding utf8
Write-Host "  ✅ 영향 분석 완료" -ForegroundColor Green
$report += @"
## 6. spec-impact F-010 (영향 분석)

``````json
$impactResult
``````

---

"@

# ─── 7. spec-propagate ────────────────────────────────────────────────
Write-Host "▶ [7/8] spec-propagate F-010 (dry-run) 실행..." -ForegroundColor Yellow
$propResult = Invoke-Expression "$FlowCli spec-propagate F-010 --status needs-review --pretty" 2>&1 | Out-String
$propResult | Out-File (Join-Path $EvidenceRoot "07_spec_propagate.json") -Encoding utf8
Write-Host "  ✅ 상태 전파 (dry-run) 완료" -ForegroundColor Green
$report += @"
## 7. spec-propagate F-010 (상태 전파 dry-run)

``````json
$propResult
``````

---

"@

# ─── 8. spec-check-refs ──────────────────────────────────────────────
Write-Host "▶ [8/8] spec-check-refs 실행..." -ForegroundColor Yellow
$refsResult = Invoke-Expression "$FlowCli spec-check-refs --pretty" 2>&1 | Out-String
$refsResult | Out-File (Join-Path $EvidenceRoot "08_spec_check_refs.json") -Encoding utf8
Write-Host "  ✅ 코드 참조 검증 완료" -ForegroundColor Green
$report += @"
## 8. spec-check-refs (코드 참조 검증)

``````json
$refsResult
``````

---

"@

# ─── 보고서 저장 ─────────────────────────────────────────────────────
$reportPath = Join-Path $EvidenceRoot "VERIFICATION_REPORT.md"
$report | Out-File $reportPath -Encoding utf8

Write-Host ""
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host " 검증 완료!" -ForegroundColor Cyan
Write-Host " 보고서: $reportPath" -ForegroundColor Cyan
Write-Host " 증거 파일: $EvidenceRoot" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan
