#requires -Version 7.0
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Path $PSScriptRoot -Parent
$projectName = 'brownNote'
$projectExeName = "$projectName.exe"
$solution = "$repoRoot\$projectName.sln"
$csproj = "$repoRoot\$projectName\$projectName.csproj"
$dotnetFramework = 'net10.0-windows'
$versionFolder = "$repoRoot\.version"
$version = "$versionFolder\version"
$versionBuild = "$versionFolder\versionBuild"
$versionTag = "$versionFolder\versionTag"
$buildNotes = "$repoRoot\buildNotes.txt"
$readme = "$repoRoot\README.md"
$publishFolder = "$repoRoot\publish"
$appPublisher = 'fosterbarnes'
$appURL = "https://github.com/$appPublisher/$projectName"
$ghRepo = "$appPublisher/$projectName"
$versionContents = ([IO.File]::ReadAllText($version)).Trim()
$versionTagContents = if (Test-Path -LiteralPath $versionTag) { ([IO.File]::ReadAllText($versionTag)).Trim() } else { '' }
$tag = if ($versionTagContents) { $versionTagContents } else { "v$versionContents" }
$buildTargets = @(
    @{ Architecture = 'x64'; RuntimeIdentifier = 'win-x64'; BinFolder = "$publishFolder\build\x64"; ExePath = "$publishFolder\build\x64\$projectExeName" }
    @{ Architecture = 'arm64'; RuntimeIdentifier = 'win-arm64'; BinFolder = "$publishFolder\build\arm64"; ExePath = "$publishFolder\build\arm64\$projectExeName" }
)
$noBom = New-Object System.Text.UTF8Encoding $false
$weztermExe = (Get-Command wezterm.exe -ErrorAction SilentlyContinue)?.Source

function readVerFile {
    param([string]$LiteralPath = $version)
    $lines = @(([IO.File]::ReadAllText($LiteralPath) -split '\r?\n' | ForEach-Object { $_.Trim() }))
    while ($lines.Count -lt 3) { $lines += '' }
    $lines
}

function writeFileNoBom {
    param([Parameter(Mandatory)][string]$LiteralPath, [Parameter(Mandatory)][string]$Content)
    [IO.File]::WriteAllText($LiteralPath, $Content, $noBom)
}

function writeVerFile {
    param(
        [Parameter(Mandatory)][string]$SemVer,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Tag,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Build
    )
    writeFileNoBom -LiteralPath $version -Content (($SemVer.Trim(), $Tag.Trim(), $Build.Trim()) -join "`n")
}

function setVerBuild {
    param([Parameter(Mandatory)][string]$Platform)
    writeFileNoBom -LiteralPath $versionBuild -Content ($Platform.Trim() + "`n")
}

function checkVerBuild {
    param([string]$Architecture)
    if (Test-Path -LiteralPath $versionBuild) { return }
    $platform = if ([string]::IsNullOrWhiteSpace($Architecture)) { 'x64' } else { $Architecture }
    setVerBuild $platform
}

function getArchitecture {
    param([string[]]$FlagArgs)
    $found = @()
    foreach ($arg in @($FlagArgs)) {
        if ([string]::IsNullOrWhiteSpace("$arg")) { continue }
        switch -Regex ("$arg".Trim()) {
            '(?i)^(x64|--x64|-x64|--64|-64)$' { $found += 'x64' }
            '(?i)^(arm64|--arm64|-arm64|--arm|-arm)$' { $found += 'arm64' }
            '(?i)^(--help|-h)$' { return 'help' }
            default { throw "Unknown architecture flag: $arg" }
        }
    }
    $unique = @($found | Select-Object -Unique)
    if ($unique.Count -gt 1) { throw "Conflicting architecture flags: $($unique -join ', ')" }
    if ($unique.Count -eq 1) { return $unique[0] }
    $null
}

function getBuildTargets {
    param([string]$Architecture)
    if (-not $Architecture) { return $buildTargets }
    $target = @($buildTargets | Where-Object Architecture -eq $Architecture)
    if (-not $target) { throw "No build target for architecture: $Architecture" }
    return ,$target[0]
}

function deleteDir {
    param([Parameter(Mandatory)][string]$Path)
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Recurse -Force }
}

function runNativeCommand {
    param([Parameter(Mandatory)][string]$FilePath, [Parameter(Mandatory)]$ArgumentList, [Parameter(Mandatory)][string]$Name)
    & $FilePath @ArgumentList
    if ($LASTEXITCODE) { throw "$Name failed (exit $LASTEXITCODE)." }
}

function writeClearedLine {
    param([Parameter(Mandatory)][string]$Text, [Parameter(Mandatory)][int]$PadWidth, [switch]$NoNewline)
    Write-Host "`r$Text$(' ' * [Math]::Max(0, $PadWidth - $Text.Length))" -NoNewline:$NoNewline
}

function closeOut {
    param([int]$Seconds = 5)
    if ($env:BASE_BUILD_PIPELINE) { return }
    if ($Seconds -lt 0) { $Seconds = 0 }
    if ($Seconds -gt 0) {
        $pad = "closing after $Seconds seconds..."
        foreach ($n in $Seconds..1) {
            writeClearedLine -Text "closing after $n seconds..." -PadWidth $pad.Length -NoNewline
            Start-Sleep -Seconds 1
        }
        writeClearedLine -Text 'closing...' -PadWidth $pad.Length
    }
    $caller = $MyInvocation.PSCommandPath
    if ([string]::IsNullOrWhiteSpace($caller)) { return }
    $argv = [Environment]::GetCommandLineArgs()
    $fileArg = $null
    for ($i = 0; $i -lt $argv.Length; $i++) {
        if ("$($argv[$i])" -match '^(?i)-File$|^(?i)-f$') {
            if ($i + 1 -lt $argv.Length) { $fileArg = $argv[$i + 1] }
            break
        }
    }
    if ([string]::IsNullOrWhiteSpace($fileArg)) { return }
    try {
        $fileFull = [IO.Path]::GetFullPath($fileArg)
        $callerFull = [IO.Path]::GetFullPath($caller)
    } catch { return }
    if (-not [string]::Equals($fileFull, $callerFull, [StringComparison]::OrdinalIgnoreCase)) { return }
    try {
        if ($env:SCRIPT_OWN_PANE -and $env:WEZTERM_PANE -and $weztermExe) { & $weztermExe @('cli', 'kill-pane', '--pane-id', $env:WEZTERM_PANE) }
    } catch { }
    [Environment]::Exit(0)
}

function openUrl {
    param([Parameter(Mandatory)][string]$Url)
    if ([string]::IsNullOrWhiteSpace($Url)) { throw 'openUrl requires a URL.' }
    Start-Process $Url
}

function buildAssetName {
    param([Parameter(Mandatory)][ValidateSet('Portable')][string]$Kind, [Parameter(Mandatory)][string]$Architecture)
    $extension = 'zip'
    "${projectName}_v${versionContents}_windows-${Architecture}.${extension}"
}

function buildAll {
    param([string]$Architecture)
    $previousPipeline = $env:BASE_BUILD_PIPELINE
    try {
        $env:BASE_BUILD_PIPELINE = '1'
        deleteDir $publishFolder
        $params = @{}; if ($Architecture) { $params.Architecture = $Architecture }
        runNativeCommand -FilePath "$PSScriptRoot\build.ps1" -ArgumentList $params -Name 'build.ps1'
        New-Item -ItemType Directory -Path $publishFolder -Force | Out-Null
        foreach ($target in (getBuildTargets $Architecture)) {
            Compress-Archive -Path "$($target.BinFolder)\*" -DestinationPath "$publishFolder\$(buildAssetName -Kind Portable -Architecture $target.Architecture)" -Force
        }
        runNativeCommand -FilePath "$PSScriptRoot\updateReadme.ps1" -ArgumentList @{} -Name 'updateReadme.ps1'
    } finally {
        $env:BASE_BUILD_PIPELINE = $previousPipeline
    }
}

Set-Location -LiteralPath $repoRoot
