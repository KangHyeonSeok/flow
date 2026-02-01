# 1부터 100까지 합계 계산
$sum = 0
1..100 | ForEach-Object { $sum += $_ }
Write-Output $sum
