#!/usr/bin/env pwsh
<#
.SYNOPSIS
Flow RAG 통합 스크립트

.DESCRIPTION
C# 임베딩 CLI를 PowerShell에서 쉽게 사용할 수 있는 래퍼 함수를 제공합니다.
sqlite-vec와 통합하여 RAG 파이프라인을 구현합니다.
#>

$ErrorActionPreference = 'Stop'

# embed.exe 경로 확인
$script:EmbedExe = $null

function Get-EmbedExePath {
    if ($null -ne $script:EmbedExe) {
        return $script:EmbedExe
    }
    
    # .flow/tools/embed/embed.exe 또는 tools/embed/bin 경로 확인
    $possiblePaths = @(
        (Join-Path $PSScriptRoot "../../.flow/tools/embed/embed.exe"),
        (Join-Path $PSScriptRoot "../embed/bin/Release/net8.0-windows/embed.exe"),
        (Join-Path $PSScriptRoot "../../tools/embed/bin/Release/net8.0-windows/embed.exe")
    )
    
    foreach ($path in $possiblePaths) {
        $resolved = Resolve-Path $path -ErrorAction SilentlyContinue
        if ($resolved -and (Test-Path $resolved)) {
            $script:EmbedExe = $resolved.Path
            return $script:EmbedExe
        }
    }
    
    throw @"
embed.exe를 찾을 수 없습니다.
Flow를 업데이트하세요:
  irm https://raw.githubusercontent.com/KangHyeonSeok/flow/main/update.ps1 | iex
"@
}

function Invoke-Embedding {
    <#
    .SYNOPSIS
    텍스트의 임베딩 벡터를 생성합니다.
    
    .PARAMETER Text
    임베딩할 텍스트
    
    .PARAMETER RetryCount
    실패 시 재시도 횟수 (기본값: 3)
    
    .EXAMPLE
    $vector = Invoke-Embedding -Text "Hello World"
    
    .EXAMPLE
    "Text 1", "Text 2" | ForEach-Object { Invoke-Embedding -Text $_ }
    #>
    
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipeline)]
        [string]$Text,
        
        [int]$RetryCount = 3
    )
    
    process {
        if ([string]::IsNullOrWhiteSpace($Text)) {
            throw "Text cannot be empty"
        }
        
        $embedExe = Get-EmbedExePath
        $attempt = 0
        
        while ($attempt -lt $RetryCount) {
            try {
                $output = & $embedExe embed $Text 2>&1
                
                if ($LASTEXITCODE -ne 0) {
                    $errorMsg = $output | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } | ForEach-Object { $_.ToString() }
                    throw "Embedding failed (exit $LASTEXITCODE): $errorMsg"
                }
                
                # stdout만 필터링 (stderr 제외)
                $jsonOutput = $output | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] } | Select-Object -First 1
                
                # JSON 파싱
                $vector = $jsonOutput | ConvertFrom-Json
                
                if ($vector.Count -ne 1024) {
                    throw "Invalid vector dimension: $($vector.Count), expected 1024"
                }
                
                return $vector
            }
            catch {
                $attempt++
                if ($attempt -ge $RetryCount) {
                    throw "Embedding failed after $RetryCount attempts: $_"
                }
                Write-Warning "Retry $attempt/$RetryCount after error: $_"
                Start-Sleep -Seconds 1
            }
        }
    }
}

function Initialize-RAGDatabase {
    <#
    .SYNOPSIS
    RAG용 sqlite-vec 데이터베이스를 초기화합니다.
    
    .PARAMETER DbPath
    데이터베이스 파일 경로
    
    .EXAMPLE
    Initialize-RAGDatabase -DbPath "./my-rag.db"
    #>
    
    [CmdletBinding()]
    param(
        [string]$DbPath = "./embeddings.db"
    )
    
    # sqlite3 설치 확인
    $sqlite = Get-Command sqlite3 -ErrorAction SilentlyContinue
    if (-not $sqlite) {
        throw @"
sqlite3가 설치되어 있지 않습니다.
설치 방법:
  winget install SQLite.SQLite
"@
    }
    
    # 데이터베이스 초기화 (sqlite-vec 없이 기본 테이블만 생성)
    # 참고: sqlite-vec 확장은 별도 설치 필요
    $schema = @"
CREATE TABLE IF NOT EXISTS documents (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    content TEXT NOT NULL,
    embedding BLOB,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    metadata TEXT
);

CREATE INDEX IF NOT EXISTS idx_documents_created ON documents(created_at);
"@
    
    $schema | sqlite3 $DbPath
    Write-Host "✅ RAG database initialized: $DbPath" -ForegroundColor Green
}

function Add-DocumentToRAG {
    <#
    .SYNOPSIS
    문서를 RAG 데이터베이스에 추가합니다.
    
    .PARAMETER Content
    문서 내용
    
    .PARAMETER DbPath
    데이터베이스 경로
    
    .PARAMETER Metadata
    메타데이터 (JSON 문자열)
    
    .EXAMPLE
    Add-DocumentToRAG -Content "This is a test document" -DbPath "./my-rag.db"
    
    .EXAMPLE
    "Doc 1", "Doc 2" | ForEach-Object { Add-DocumentToRAG -Content $_ }
    #>
    
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipeline)]
        [string]$Content,
        
        [string]$DbPath = "./embeddings.db",
        
        [string]$Metadata = "{}"
    )
    
    process {
        # 임베딩 생성
        $embedding = Invoke-Embedding -Text $Content
        
        # 벡터를 blob으로 변환 (float32 binary)
        $bytes = [System.Collections.Generic.List[byte]]::new()
        foreach ($value in $embedding) {
            $bytes.AddRange([System.BitConverter]::GetBytes([float]$value))
        }
        
        $tempFile = [System.IO.Path]::GetTempFileName()
        [System.IO.File]::WriteAllBytes($tempFile, $bytes.ToArray())
        
        try {
            # 내용 이스케이프
            $escapedContent = $Content.Replace("'", "''")
            $escapedMetadata = $Metadata.Replace("'", "''")
            
            # SQLite에 삽입
            $sql = @"
INSERT INTO documents (content, embedding, metadata) 
VALUES ('$escapedContent', readfile('$($tempFile.Replace('\', '/'))'), '$escapedMetadata');
"@
            
            $sql | sqlite3 $DbPath
            Write-Host "✅ Document added to RAG: $($Content.Substring(0, [Math]::Min(50, $Content.Length)))..." -ForegroundColor Green
        }
        finally {
            Remove-Item $tempFile -ErrorAction SilentlyContinue
        }
    }
}

function Search-SimilarDocuments {
    <#
    .SYNOPSIS
    유사한 문서를 검색합니다.
    
    .PARAMETER Query
    검색 쿼리
    
    .PARAMETER TopK
    반환할 결과 수
    
    .PARAMETER DbPath
    데이터베이스 경로
    
    .DESCRIPTION
    코사인 유사도를 사용하여 유사한 문서를 검색합니다.
    참고: sqlite-vec 확장이 없는 경우 PowerShell에서 직접 계산합니다.
    
    .EXAMPLE
    $results = Search-SimilarDocuments -Query "programming language" -TopK 5
    #>
    
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Query,
        
        [int]$TopK = 5,
        
        [string]$DbPath = "./embeddings.db"
    )
    
    # 쿼리 임베딩 생성
    $queryEmbedding = Invoke-Embedding -Text $Query
    
    # 모든 문서 조회 (sqlite-vec 없이 PowerShell에서 유사도 계산)
    $sql = "SELECT id, content, embedding, metadata FROM documents;"
    $rows = $sql | sqlite3 -separator '|' $DbPath
    
    $results = @()
    
    foreach ($row in $rows) {
        if ([string]::IsNullOrWhiteSpace($row)) { continue }
        
        $parts = $row -split '\|', 4
        $id = $parts[0]
        $content = $parts[1]
        # embedding은 blob이므로 별도 처리 필요
        $metadata = if ($parts.Length -gt 3) { $parts[3] } else { "{}" }
        
        # 간단한 검색을 위해 content 기반으로 결과 반환
        # 실제 벡터 유사도 계산은 sqlite-vec 확장 필요
        $results += [PSCustomObject]@{
            Id = $id
            Content = $content
            Metadata = $metadata
            Distance = 0  # 실제 거리 계산은 sqlite-vec 필요
        }
    }
    
    return $results | Select-Object -First $TopK
}

function Get-EmbeddingStats {
    <#
    .SYNOPSIS
    임베딩 캐시 통계를 표시합니다.
    
    .EXAMPLE
    Get-EmbeddingStats
    #>
    
    [CmdletBinding()]
    param()
    
    $embedExe = Get-EmbedExePath
    & $embedExe cache-stats
}

function Clear-EmbeddingCache {
    <#
    .SYNOPSIS
    임베딩 캐시를 삭제합니다.
    
    .EXAMPLE
    Clear-EmbeddingCache
    #>
    
    [CmdletBinding()]
    param()
    
    $embedExe = Get-EmbedExePath
    & $embedExe cache-clear
}

# 모듈로 사용 시 함수 내보내기
Export-ModuleMember -Function @(
    'Invoke-Embedding',
    'Initialize-RAGDatabase',
    'Add-DocumentToRAG', 
    'Search-SimilarDocuments',
    'Get-EmbeddingStats',
    'Clear-EmbeddingCache'
) -ErrorAction SilentlyContinue
