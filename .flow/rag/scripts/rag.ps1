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
$script:SqliteExe = $null

function Get-EmbedExePath {
    if ($null -ne $script:EmbedExe) {
        return $script:EmbedExe
    }
    
    # .flow/rag/bin/embed.exe 우선 확인, 그 외 기존 경로 확인
    $possiblePaths = @(
        (Join-Path $PSScriptRoot "..\bin\embed.exe"),
        (Join-Path $PSScriptRoot "..\bin\win-x64\embed.exe"),
        (Join-Path $PSScriptRoot "..\..\..\tools\embed\embed.exe"),
        (Join-Path $PSScriptRoot "..\..\..\tools\embed\bin\Release\net8.0-windows\embed.exe"),
        (Join-Path $PSScriptRoot "..\..\..\tools\embed\bin\Debug\net8.0-windows\embed.exe")
    )
    
    foreach ($path in $possiblePaths) {
        $resolved = Resolve-Path $path -ErrorAction SilentlyContinue
        if ($resolved -and (Test-Path $resolved)) {
            $script:EmbedExe = $resolved.Path
            return $script:EmbedExe
        }
    }

    $fromPath = Get-Command embed.exe -ErrorAction SilentlyContinue
    if ($fromPath) {
        $script:EmbedExe = $fromPath.Path
        return $script:EmbedExe
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

function Get-SqliteVecPath {
    <#
    .SYNOPSIS
    sqlite-vec 확장 DLL 경로를 반환합니다.
    #>
    
    $localPath = Join-Path $PSScriptRoot "..\bin\vec0.dll"
    if (Test-Path $localPath) {
        return $localPath
    }

    $extensionDir = Join-Path $env:LOCALAPPDATA "flow-embed" "extensions"
    $vecPath = Join-Path $extensionDir "vec0.dll"

    if (Test-Path $vecPath) {
        return $vecPath
    }

    return $null
}

function Get-DefaultDbPath {
    <#
    .SYNOPSIS
    기본 DB 경로를 반환합니다.
    #>
    
    $dbDir = Join-Path $PSScriptRoot "..\db"
    if (-not (Test-Path $dbDir)) {
        New-Item -ItemType Directory -Path $dbDir -Force | Out-Null
    }
    return Join-Path $dbDir "local.db"
}

function Get-Sqlite3ExePath {
    if ($null -ne $script:SqliteExe) {
        return $script:SqliteExe
    }

    if ($env:FLOW_SQLITE3_PATH -and (Test-Path $env:FLOW_SQLITE3_PATH)) {
        $script:SqliteExe = $env:FLOW_SQLITE3_PATH
        return $script:SqliteExe
    }

    $pkgRoot = Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages"
    if (Test-Path $pkgRoot) {
        $sqlitePkgs = Get-ChildItem -Path $pkgRoot -Directory -Filter "SQLite.SQLite_*" -ErrorAction SilentlyContinue
        foreach ($pkg in $sqlitePkgs) {
            $exe = Get-ChildItem -Path $pkg.FullName -Filter "sqlite3.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($exe) {
                $script:SqliteExe = $exe.FullName
                return $script:SqliteExe
            }
        }
    }

    $wingetLink = Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Links\sqlite3.exe"
    if (Test-Path $wingetLink) {
        $script:SqliteExe = $wingetLink
        return $script:SqliteExe
    }

    $sqlite = Get-Command sqlite3 -ErrorAction SilentlyContinue
    if ($sqlite) {
        $script:SqliteExe = $sqlite.Source
        return $script:SqliteExe
    }

    return $null
}

function Initialize-RAGDatabase {
    <#
    .SYNOPSIS
    RAG용 sqlite-vec 데이터베이스를 초기화합니다.
    
    .PARAMETER DbPath
    데이터베이스 파일 경로 (기본값: .flow/rag/db/local.db)
    
    .PARAMETER SkipVec
    sqlite-vec 확장 로드를 건너뜁니다. (벡터 검색 비활성화)
    
    .EXAMPLE
    Initialize-RAGDatabase
    
    .EXAMPLE
    Initialize-RAGDatabase -DbPath "./my-rag.db"
    #>
    
    [CmdletBinding()]
    param(
        [string]$DbPath,
        
        [switch]$SkipVec
    )
    
    if ([string]::IsNullOrEmpty($DbPath)) {
        $DbPath = Get-DefaultDbPath
    }
    
        # sqlite3 설치 확인
        $sqliteExe = Get-Sqlite3ExePath
        if (-not $sqliteExe) {
        throw @"
sqlite3가 설치되어 있지 않습니다.
설치 방법:
  winget install SQLite.SQLite
"@
    }
    
    # sqlite-vec 확장 경로 확인
    $vecPath = Get-SqliteVecPath
    $vecLoaded = $false
    
    if (-not $SkipVec -and $vecPath) {
        # 경로의 백슬래시를 슬래시로 변환 (SQLite 호환)
        $vecPathSql = $vecPath.Replace('\', '/')
        
        # sqlite-vec 확장 로드 및 스키마 초기화
        $schemaWithVec = @"
.load $vecPathSql

-- 원본 문서 및 태그 저장
CREATE TABLE IF NOT EXISTS documents (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    content TEXT NOT NULL,
    canonical_tags TEXT,
    metadata TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_documents_created ON documents(created_at);

-- sqlite-vec 벡터 테이블 (1024차원 - BGE-M3 모델)
CREATE VIRTUAL TABLE IF NOT EXISTS vec_documents USING vec0(
    embedding float[1024]
);
"@
        
        try {
            $schemaWithVec | & $sqliteExe $DbPath 2>&1
            if ($LASTEXITCODE -eq 0) {
                $vecLoaded = $true
            }
        }
        catch {
            Write-Warning "sqlite-vec 로드 실패, 기본 스키마로 대체합니다: $_"
        }
    }
    
    if (-not $vecLoaded) {
        # sqlite-vec 없이 기본 테이블만 생성
        $schemaBasic = @"
-- 원본 문서 및 태그 저장
CREATE TABLE IF NOT EXISTS documents (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    content TEXT NOT NULL,
    canonical_tags TEXT,
    embedding BLOB,
    metadata TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_documents_created ON documents(created_at);
"@
        
        $schemaBasic | & $sqliteExe $DbPath
        Write-Host "⚠️ RAG database initialized (without sqlite-vec): $DbPath" -ForegroundColor Yellow
        Write-Host "   sqlite-vec 확장이 없어 벡터 검색이 PowerShell에서 수행됩니다." -ForegroundColor Yellow
        return
    }
    
    Write-Host "✅ RAG database initialized with sqlite-vec: $DbPath" -ForegroundColor Green
}

function Add-DocumentToRAG {
    <#
    .SYNOPSIS
    문서를 RAG 데이터베이스에 추가합니다.
    
    .PARAMETER Content
    문서 내용
    
    .PARAMETER DbPath
    데이터베이스 경로
    
    .PARAMETER Tags
    태그 목록 (문자열 배열 또는 쉼표 구분 문자열)
    
    .PARAMETER Metadata
    메타데이터 (JSON 문자열)
    
    .EXAMPLE
    Add-DocumentToRAG -Content "This is a test document"
    
    .EXAMPLE
    Add-DocumentToRAG -Content "AI document" -Tags "neural_network,machine_learning"
    
    .EXAMPLE
    "Doc 1", "Doc 2" | ForEach-Object { Add-DocumentToRAG -Content $_ }
    #>
    
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipeline)]
        [string]$Content,
        
        [string]$DbPath,
        
        [string[]]$Tags,
        
        [string]$Metadata = "{}"
    )
    
    process {
        if ([string]::IsNullOrEmpty($DbPath)) {
            $DbPath = Get-DefaultDbPath
        }
        
        # 임베딩 생성
        $embedding = Invoke-Embedding -Text $Content
        
        # 태그 처리
        $canonicalTags = ""
        if ($Tags) {
            # 쉼표로 구분된 문자열이면 분리
            $tagList = @()
            foreach ($tag in $Tags) {
                $tagList += $tag -split ',' | ForEach-Object { $_.Trim().ToLower() -replace '\s+', '_' }
            }
            $canonicalTags = ($tagList | ConvertTo-Json -Compress)
        }
        
        # sqlite-vec 사용 여부 확인
        $vecPath = Get-SqliteVecPath
        $sqliteExe = Get-Sqlite3ExePath
        if (-not $sqliteExe) {
            throw "sqlite3가 설치되어 있지 않습니다. winget install SQLite.SQLite로 설치하세요."
        }
        
        if ($vecPath) {
            # sqlite-vec 활성화된 경우: documents와 vec_documents에 각각 삽입
            $escapedContent = $Content.Replace("'", "''")
            $escapedMetadata = $Metadata.Replace("'", "''")
            $escapedTags = $canonicalTags.Replace("'", "''")
            
            # 벡터를 JSON 배열로 변환
            $vectorJson = "[$($embedding -join ',')]"
            
            $vecPathSql = $vecPath.Replace('\', '/')
            
            $sql = @"
.load $vecPathSql

INSERT INTO documents (content, canonical_tags, metadata) 
VALUES ('$escapedContent', '$escapedTags', '$escapedMetadata');

INSERT INTO vec_documents (rowid, embedding)
VALUES (last_insert_rowid(), '$vectorJson');
"@
            
            $sql | & $sqliteExe $DbPath
        }
        else {
            # sqlite-vec 없이: embedding을 BLOB으로 저장
            $bytes = [System.Collections.Generic.List[byte]]::new()
            foreach ($value in $embedding) {
                $bytes.AddRange([System.BitConverter]::GetBytes([float]$value))
            }
            
            $tempFile = [System.IO.Path]::GetTempFileName()
            [System.IO.File]::WriteAllBytes($tempFile, $bytes.ToArray())
            
            try {
                $escapedContent = $Content.Replace("'", "''")
                $escapedMetadata = $Metadata.Replace("'", "''")
                $escapedTags = $canonicalTags.Replace("'", "''")
                
                $sql = @"
INSERT INTO documents (content, canonical_tags, embedding, metadata) 
VALUES ('$escapedContent', '$escapedTags', readfile('$($tempFile.Replace('\', '/'))'), '$escapedMetadata');
"@
                
                $sql | & $sqliteExe $DbPath
            }
            finally {
                Remove-Item $tempFile -ErrorAction SilentlyContinue
            }
        }
        
        Write-Host "✅ Document added to RAG: $($Content.Substring(0, [Math]::Min(50, $Content.Length)))..." -ForegroundColor Green
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
    
    .PARAMETER Tags
    검색 시 태그 필터 (하이브리드 검색)
    
    .DESCRIPTION
    sqlite-vec가 활성화된 경우 하이브리드 검색 (태그 + 벡터)을 수행합니다.
    그렇지 않으면 PowerShell에서 코사인 유사도를 직접 계산합니다.
    
    .EXAMPLE
    $results = Search-SimilarDocuments -Query "programming language" -TopK 5
    
    .EXAMPLE
    $results = Search-SimilarDocuments -Query "AI" -Tags "neural_network,deep_learning"
    #>
    
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Query,
        
        [int]$TopK = 5,
        
        [string]$DbPath,
        
        [string[]]$Tags
    )
    
    if ([string]::IsNullOrEmpty($DbPath)) {
        $DbPath = Get-DefaultDbPath
    }
    
    # 쿼리 임베딩 생성
    $queryEmbedding = Invoke-Embedding -Text $Query
    
    # sqlite-vec 확장 확인
    $vecPath = Get-SqliteVecPath
    
    if ($vecPath) {
        # sqlite-vec 활성화: 하이브리드 검색
        $vecPathSql = $vecPath.Replace('\', '/')
        $vectorJson = "[$($queryEmbedding -join ',')]"
        
        # 태그 스코어 계산 SQL 구성
        $tagScoreSql = "0"
        if ($Tags) {
            $tagList = @()
            foreach ($tag in $Tags) {
                $tagList += $tag -split ',' | ForEach-Object { $_.Trim().ToLower() -replace '\s+', '_' }
            }
            $tagConditions = $tagList | ForEach-Object { "(d.canonical_tags LIKE '%$_%')" }
            $tagScoreSql = "(" + ($tagConditions -join ' + ') + ")"
        }
        
        $sql = @"
.load $vecPathSql
.mode json

SELECT 
    d.id,
    d.content,
    d.canonical_tags,
    d.metadata,
    $tagScoreSql AS tag_score,
    v.distance
FROM vec_documents v
JOIN documents d ON v.rowid = d.id
WHERE v.embedding MATCH '$vectorJson'
ORDER BY tag_score DESC, distance ASC
LIMIT $TopK;
"@
        
        $sqliteExe = Get-Sqlite3ExePath
        if (-not $sqliteExe) {
            throw "sqlite3가 설치되어 있지 않습니다. winget install SQLite.SQLite로 설치하세요."
        }

        $jsonOutput = $sql | & $sqliteExe $DbPath 2>&1
        
        if ($LASTEXITCODE -eq 0 -and $jsonOutput) {
            try {
                $results = $jsonOutput | ConvertFrom-Json
                return $results
            }
            catch {
                Write-Warning "JSON 파싱 실패, 텍스트 모드로 전환: $_"
            }
        }
    }
    
    # sqlite-vec 없이: PowerShell에서 유사도 계산
    $sql = "SELECT id, content, canonical_tags, metadata FROM documents;"
    $rows = $sql | & $sqliteExe -separator '|' $DbPath
    
    $results = @()
    
    foreach ($row in $rows) {
        if ([string]::IsNullOrWhiteSpace($row)) { continue }
        
        $parts = $row -split '\|', 4
        $id = $parts[0]
        $content = $parts[1]
        $docTags = $parts[2]
        $metadata = if ($parts.Length -gt 3) { $parts[3] } else { "{}" }
        
        # 태그 스코어 계산
        $tagScore = 0
        if ($Tags -and $docTags) {
            foreach ($tag in $Tags) {
                $tagList = $tag -split ',' | ForEach-Object { $_.Trim().ToLower() -replace '\s+', '_' }
                foreach ($t in $tagList) {
                    if ($docTags -match $t) {
                        $tagScore++
                    }
                }
            }
        }
        
        $results += [PSCustomObject]@{
            Id = $id
            Content = $content
            CanonicalTags = $docTags
            Metadata = $metadata
            TagScore = $tagScore
            Distance = 0  # 실제 거리 계산은 BLOB 파싱 필요
        }
    }
    
    # 태그 스코어 내림차순, 거리 오름차순 정렬
    return $results | Sort-Object -Property @{Expression="TagScore";Descending=$true}, @{Expression="Distance";Ascending=$true} | Select-Object -First $TopK
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
if ($MyInvocation.Line -match 'Import-Module') {
    Export-ModuleMember -Function @(
        'Invoke-Embedding',
        'Initialize-RAGDatabase',
        'Add-DocumentToRAG', 
        'Search-SimilarDocuments',
        'Get-EmbeddingStats',
        'Clear-EmbeddingCache',
        'Get-SqliteVecPath',
        'Get-DefaultDbPath'
    )
}
