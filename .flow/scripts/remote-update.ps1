# 업데이트 스크립트: remote-update.ps1
# 원격 저장소의 update.ps1을 실행하여 최신 상태로 업데이트합니다.

$updateUrl = "https://raw.githubusercontent.com/KangHyeonSeok/flow/main/update.ps1"

try {
    Write-Host "[INFO] 원격 update.ps1을 다운로드하여 실행합니다: $updateUrl"
    irm $updateUrl | iex
    Write-Host "[SUCCESS] 업데이트가 완료되었습니다."
} catch {
    Write-Error "[ERROR] 업데이트 중 오류 발생: $_"
    exit 1
}
