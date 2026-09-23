#requires -Version 7.0
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\scriptHelper.ps1"
Set-Location -LiteralPath $repoRoot
if (-not (Test-Path -LiteralPath $readme)) { throw "README not found: $readme" }
$content = [IO.File]::ReadAllText($readme)
$start = '<!-- Quick Reference -->'
$end = '<!-- End Quick Reference -->'
$startIndex = $content.IndexOf($start, [StringComparison]::Ordinal)
$endIndex = $content.IndexOf($end, [StringComparison]::Ordinal)
if ($startIndex -lt 0 -or $endIndex -lt $startIndex) { throw 'README Quick Reference markers are missing.' }
$lines = @(
    $start
    ''
    "- Windows x64 portable: $appURL/releases/download/$tag/$(buildAssetName -Kind Portable -Architecture x64)"
    "- Windows ARM64 portable: $appURL/releases/download/$tag/$(buildAssetName -Kind Portable -Architecture arm64)"
    $end
)
$prefix = $content.Substring(0, $startIndex) ; $suffix = $content.Substring($endIndex + $end.Length)
writeFileNoBom -LiteralPath $readme -Content ($prefix + ($lines -join "`n") + $suffix)
closeOut 0
