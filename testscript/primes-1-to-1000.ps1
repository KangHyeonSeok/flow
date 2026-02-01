# 1부터 1000까지의 소수 출력
$primes = @()

function Test-Prime {
    param(
        [int]$n
    )

    if ($n -lt 2) { return $false }
    if ($n -eq 2) { return $true }
    if ($n % 2 -eq 0) { return $false }

    $limit = [math]::Floor([math]::Sqrt($n))
    for ($i = 3; $i -le $limit; $i += 2) {
        if ($n % $i -eq 0) { return $false }
    }

    return $true
}

for ($n = 1; $n -le 1000; $n++) {
    if (Test-Prime -n $n) {
        $primes += $n
    }
}

Write-Output ($primes -join ' ')
