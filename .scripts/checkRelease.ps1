#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    "$PSScriptRoot\pushRelease.ps1", [ref]$tokens, [ref]$errors)
if ($errors) { throw ($errors -join "`n") }
# Run the real operator body with mocked external commands and staged assets.
$body = [scriptblock]::Create(($ast.EndBlock.Statements |
    Select-Object -Skip 3 | ForEach-Object { $_.Extent.Text }) -join "`n")

function checkRelease {
    param([string]$ReleasesJson, [string]$ExpectedNotes, [switch]$DryRun,
        [switch]$FailQuery)
    $tag = 'v0.2.2'; $ghRepo = 'fosterbarnes/brownNote'
    $appURL = "https://github.com/$ghRepo"; $publishFolder = 'mock'
    $calls = [Collections.Generic.List[object]]::new()
    function Get-ChildItem { @(@{ FullName = 'arm64.exe' }, @{ FullName = 'x64.zip' }) }
    function runNativeCommand {
        param($FilePath, $ArgumentList, $Name)
        $calls.Add(@{ File = $FilePath; Args = $ArgumentList })
        if ($Name -eq 'gh release list') {
            if ($FailQuery) { throw 'query failed' }
            if ('--exclude-drafts' -notin $ArgumentList -or
                '--exclude-pre-releases' -notin $ArgumentList) { throw 'Missing release filters.' }
            return $ReleasesJson
        }
    }
    function openUrl { param($Url) }
    function closeOut { param($Seconds) }
    try { & $body | Out-Null } catch {
        if (-not $FailQuery -or $_.Exception.Message -ne 'query failed') { throw }
    }
    if ($DryRun -or $FailQuery) {
        if ($calls.Count -ne 1) { throw 'Dry run or failed query reached publication.' }
        return
    }
    $release = $calls[3].Args
    if ($calls.Count -ne 4 -or $release[0] -ne 'release' -or $release[1] -ne 'create' -or
        $release[$release.IndexOf('--title') + 1] -ne $tag -or '--latest' -notin $release -or
        $release[$release.IndexOf('--notes') + 1] -ne $ExpectedNotes -or
        '--generate-notes' -in $release -or $release[-2] -ne 'arm64.exe' -or
        $release[-1] -ne 'x64.zip') { throw 'Release arguments do not match the expected format.' }
}

$releases = '[{"tagName":"v0.2.0","publishedAt":"2026-10-01T00:00:00Z"},{"tagName":"v0.2.2","publishedAt":"2026-10-09T00:00:00Z"},{"tagName":"v0.2.1","publishedAt":"2026-10-08T00:00:00Z"}]'
checkRelease $releases '**Full Changelog**: [v0.2.1...v0.2.2](https://github.com/fosterbarnes/brownNote/compare/v0.2.1...v0.2.2)'
checkRelease '[]' ''
checkRelease $releases '' -DryRun
checkRelease '[]' '' -FailQuery
'Release format checks passed.'
