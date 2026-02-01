# Calculates 10 factorial and prints the result
$number = 10
$result = 1
for ($i = 1; $i -le $number; $i++) {
    $result *= $i
}

Write-Output "$number! = $result"
