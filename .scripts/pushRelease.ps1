#requires -Version 7.0
param([Alias('n')][switch]$DryRun)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\scriptHelper.ps1"
Set-Location -LiteralPath $repoRoot
$assets = @(Get-ChildItem -LiteralPath $publishFolder -File)
if (-not $assets) { throw "No release assets found in $publishFolder" }
$assetArgs = @($assets | ForEach-Object FullName)

$releasesJson = runNativeCommand gh @('release', 'list', '--repo', $ghRepo,
    '--exclude-drafts', '--exclude-pre-releases', '--json', 'tagName,publishedAt',
    '--limit', '100') 'gh release list'
$previousRelease = $releasesJson | ConvertFrom-Json |
    Where-Object tagName -ne $tag | Sort-Object publishedAt -Descending | Select-Object -First 1
$releaseNotes = if ($previousRelease) {
    $comparison = "$($previousRelease.tagName)...$tag"
    "**Full Changelog**: [$comparison]($appURL/compare/$comparison)"
} else { '' }
$releaseArgs = @('release', 'create', $tag, '--title', $tag, '--repo', $ghRepo,
    '--latest', '--notes', $releaseNotes)
$releaseUrl = "$appURL/releases/tag/$tag"
if ($DryRun) {
    Write-Host "Dry run: git tag -f $tag"
    Write-Host "Dry run: git push origin refs/tags/$tag --force"
    Write-Host "Dry run: gh $((($releaseArgs + $assetArgs) -join ' '))"
    Write-Host "Dry run: open $releaseUrl"
    return
}
runNativeCommand git @('tag', '-f', $tag) 'git tag'
runNativeCommand git @('push', 'origin', "refs/tags/$tag", '--force') 'git push tag'
runNativeCommand gh ($releaseArgs + $assetArgs) 'gh release create'
openUrl $releaseUrl
closeOut 0
