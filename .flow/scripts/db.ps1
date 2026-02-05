#!/usr/bin/env pwsh
<#
.SYNOPSIS
Flow RAG 데이터베이스 CLI

.DESCRIPTION
sqlite-vec 기반 벡터 저장 및 하이브리드 검색을 위한 CLI 인터페이스.

.PARAMETER Add
추가할 텍스트 (--add "텍스트")

.PARAMETER Query
검색할 텍스트 (--query "질문")

.PARAMETER Tags
태그 목록 (쉼표 구분, --tags "tag1,tag2")

.PARAMETER Metadata
메타데이터 JSON (--metadata '{"key":"value"}')

.PARAMETER TopK
검색 결과 수 (--topk 5)

.PARAMETER DbPath
데이터베이스 경로 (--db "path/to/db")

.PARAMETER Init
데이터베이스 초기화 (--init)

.EXAMPLE
./.flow/scripts/db.ps1 --add "오늘 날씨는 맑습니다."

.EXAMPLE
./.flow/scripts/db.ps1 --add "AI 기술 문서" --tags "artificial_intelligence,machine_learning"

.EXAMPLE
./.flow/scripts/db.ps1 --query "날씨 어때?" --topk 3

.EXAMPLE
./.flow/scripts/db.ps1 --init
#>

[CmdletBinding()]
param(
    [string]$Add,
    [string]$Query,
    [string]$Tags,
    [string]$Metadata = "{}",
    [int]$TopK = 5,
    [string]$DbPath,
    [switch]$Init,
    [Alias('h')]
    [switch]$Help,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RawArgs
)

$ErrorActionPreference = 'Stop'

[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

# 인자 파서 로드
$argParser = Join-Path $PSScriptRoot "db.args.ps1"
if (-not (Test-Path $argParser)) {
    Write-Error "db.args.ps1을 찾을 수 없습니다: $argParser"
    exit 1
}
. $argParser

$boundKeys = @($PSBoundParameters.Keys | Where-Object { $_ -ne 'RawArgs' })
if ($boundKeys.Count -gt 0) {
    $parsed = [pscustomobject]@{
        Add = $Add
        Query = $Query
        Tags = $Tags
        Metadata = $Metadata
        TopK = $TopK
        DbPath = $DbPath
        Init = [bool]$Init
        ShowHelp = [bool]$Help
        HasArgs = $true
    }
}
else {
    $parsed = Parse-DbArgs -Args $RawArgs -InvocationLine $MyInvocation.Line -ScriptPath $PSCommandPath
}

$Add = $parsed.Add
$Query = $parsed.Query
$Tags = $parsed.Tags
$Metadata = $parsed.Metadata
$TopK = $parsed.TopK
$DbPath = $parsed.DbPath
$Init = $parsed.Init
$ShowHelp = $parsed.ShowHelp
$HasArgs = $parsed.HasArgs

# RAG 모듈 로드
$ragScript = Join-Path $PSScriptRoot "..\rag\scripts\rag.ps1"
if (-not (Test-Path $ragScript)) {
    Write-Error "rag.ps1을 찾을 수 없습니다: $ragScript"
    exit 1
}
. $ragScript

# 도움말 표시
if ($ShowHelp -or (-not $HasArgs -and -not $Init)) {
    Write-Host @"

Flow RAG Database CLI
=====================

사용법:
    ./.flow/scripts/db.ps1 -add "<텍스트>" [-tags "tag1,tag2"] [-metadata "<json>"]
    ./.flow/scripts/db.ps1 -query "<질문>" [-topk N] [-tags "tag1,tag2"]
    ./.flow/scripts/db.ps1 -init [-db "<경로>"]

옵션:
    -add              추가할 텍스트
    -query            검색할 질문
    -tags             태그 목록 (쉼표 구분)
    -metadata         메타데이터 JSON
    -topk             검색 결과 수 (기본값: 5)
    -db               데이터베이스 경로
    -init             데이터베이스 초기화
    -help, -h         도움말 표시

예시:
    ./.flow/scripts/db.ps1 -add "오늘 날씨는 맑습니다."
    ./.flow/scripts/db.ps1 -add "AI 기술" -tags "artificial_intelligence,deep_learning"
    ./.flow/scripts/db.ps1 -query "날씨 어때?" -topk 3
    ./.flow/scripts/db.ps1 -init

"@
    exit 0
}

# 데이터베이스 경로 설정
if ([string]::IsNullOrEmpty($DbPath)) {
    $DbPath = Get-DefaultDbPath
}

# 초기화 모드
if ($Init) {
    try {
        Initialize-RAGDatabase -DbPath $DbPath
        exit 0
    }
    catch {
        Write-Error "데이터베이스 초기화 실패: $_"
        exit 1
    }
}

# Add 모드
if (-not [string]::IsNullOrEmpty($Add)) {
    try {
        $tagArray = @()
        if (-not [string]::IsNullOrEmpty($Tags)) {
            $tagArray = $Tags -split '[,\s]+' | Where-Object { $_ -and $_.Trim() } | ForEach-Object { $_.Trim() }
        }

        Add-DocumentToRAG -Content $Add -DbPath $DbPath -Tags $tagArray -Metadata $Metadata
        exit 0
    }
    catch {
        Write-Error "문서 추가 실패: $_"
        exit 1
    }
}

# Query 모드
if (-not [string]::IsNullOrEmpty($Query)) {
    try {
        $tagArray = @()
        if (-not [string]::IsNullOrEmpty($Tags)) {
            $tagArray = $Tags -split '[,\s]+' | Where-Object { $_ -and $_.Trim() } | ForEach-Object { $_.Trim() }
        }

        $results = Search-SimilarDocuments -Query $Query -TopK $TopK -DbPath $DbPath -Tags $tagArray

        if ($results -and $results.Count -gt 0) {
            Write-Host ""
            Write-Host "검색 결과 (상위 ${TopK}개):" -ForegroundColor Cyan
            Write-Host "=========================" -ForegroundColor Cyan
            
            $i = 1
            foreach ($result in $results) {
                Write-Host ""
                Write-Host "[$i] " -NoNewline -ForegroundColor Yellow
                
                # Content 표시
                $content = if ($result.Content) { $result.Content } else { $result.content }
                $preview = if ($content.Length -gt 100) { $content.Substring(0, 100) + "..." } else { $content }
                Write-Host $preview
                
                # 태그 표시
                $tags = if ($result.CanonicalTags) { $result.CanonicalTags } elseif ($result.canonical_tags) { $result.canonical_tags } else { "" }
                if ($tags) {
                    Write-Host "    Tags: " -NoNewline -ForegroundColor DarkGray
                    Write-Host $tags -ForegroundColor DarkCyan
                }
                
                # 스코어 표시
                $tagScore = if ($null -ne $result.TagScore) { $result.TagScore } elseif ($null -ne $result.tag_score) { $result.tag_score } else { 0 }
                $distance = if ($null -ne $result.Distance) { $result.Distance } elseif ($null -ne $result.distance) { $result.distance } else { 0 }
                
                Write-Host "    TagScore: $tagScore, Distance: $([math]::Round($distance, 4))" -ForegroundColor DarkGray
                
                $i++
            }
            Write-Host ""
        }
        else {
            Write-Host "검색 결과가 없습니다." -ForegroundColor Yellow
        }
        
        exit 0
    }
    catch {
        Write-Error "검색 실패: $_"
        exit 1
    }
}

# 아무 동작도 지정되지 않은 경우
Write-Host "동작을 지정하세요. --help로 사용법을 확인하세요." -ForegroundColor Yellow
exit 1
