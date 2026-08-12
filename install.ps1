<#
    Batman: Arkham City - Archipelago  |  installer

    WINDOWS ONLY. Batman: Arkham City GAME OF THE YEAR EDITION only.
    The non-GOTY build has a different executable and is not supported.

    This script tells you everything it is going to do before it does any of
    it, and does not touch your machine until you type YES.

        .\install.ps1              install (asks first)
        .\install.ps1 -Check       verify an existing install, change nothing
        .\install.ps1 -DryRun      show every action, change nothing
        .\install.ps1 -SkipBmSDK   you already have BmSDK working
        .\install.ps1 -GamePath "D:\path\to\Batman Arkham City GOTY"
#>
[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$DryRun,
    [switch]$SkipBmSDK,
    [switch]$Yes,
    [string]$GamePath,
    [string]$ArchipelagoPath,
    [string]$PopTrackerPacksPath
)

$ErrorActionPreference = 'Stop'
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path

# Official BmSDK sources. Nothing is bundled - everything is fetched from the
# project's own GitHub releases.
$BmSdkRepo       = 'Team-BmSDK/BmSDK-AC'
$BmSdkLatestApi  = "https://api.github.com/repos/$BmSdkRepo/releases/latest"
$CompatPatchUrl  = "https://github.com/$BmSdkRepo/releases/download/v0.15.1/CompatibilityPatch.zip"

$PlayerScripts = @('ApBridge.cs','ApPaths.cs','StripGadgets.cs',
                   'UpgradePool.cs','CounterLock.cs','RiddlerHook.cs')

function Say  { param($m) Write-Host $m }
function Head { param($m) Write-Host ''; Write-Host $m -ForegroundColor Cyan }
function OK   { param($m) Write-Host "  [ok]   $m" -ForegroundColor Green }
function Warn { param($m) Write-Host "  [warn] $m" -ForegroundColor Yellow }
function Bad  { param($m) Write-Host "  [X]    $m" -ForegroundColor Red }
function Act  { param($m) Write-Host "  ->     $m" }

# ---------------------------------------------------------------- disclosure

function Show-Manifest {
    Write-Host ''
    Write-Host '===============================================================' -ForegroundColor Cyan
    Write-Host ' Batman: Arkham City - Archipelago  |  installer' -ForegroundColor Cyan
    Write-Host '===============================================================' -ForegroundColor Cyan
    Write-Host ''
    Write-Host ' PLATFORM: Windows only.'
    Write-Host ' GAME    : Batman: Arkham City GAME OF THE YEAR EDITION (Steam).'
    Write-Host '           The non-GOTY build is not supported.'
    Write-Host ''
    Write-Host ' Everything this installer does, in full:' -ForegroundColor White
    Write-Host ''
    Write-Host ' READS'
    Write-Host '   - Steam''s libraryfolders.vdf, to find where the game is installed'
    Write-Host '   - your Documents and Steam userdata folders, to find your saves'
    Write-Host ''
    Write-Host ' DOWNLOADS (only if BmSDK is missing, and only from the official repo)'
    Write-Host "   - $BmSdkLatestApi"
    Write-Host "   - $CompatPatchUrl"
    Write-Host '     BmSDK is MIT licensed and is NOT bundled with this project.'
    Write-Host '     The compatibility patch is a modified game executable supplied'
    Write-Host '     by the BmSDK project - it is downloaded from them, never from us.'
    Write-Host ''
    Write-Host ' WRITES INTO YOUR GAME FOLDER'
    Write-Host '   - Binaries\ and BmGame\        (BmSDK, merged in)'
    Write-Host '   - Binaries\Win32\BatmanAC.exe  (REPLACED by the compatibility patch)'
    Write-Host '     * your original is copied to BatmanAC.exe.original_backup first'
    Write-Host '     * this is asked separately, and you can say no'
    Write-Host "   - BmGame\Scripts\  ($($PlayerScripts.Count) .cs files from this project)"
    Write-Host ''
    Write-Host ' WRITES ELSEWHERE'
    Write-Host '   - Archipelago custom_worlds\batman_arkham_city.apworld'
    Write-Host '   - Archipelago Players\BatmanArkhamCity.yaml   (only if absent)'
    Write-Host '   - your save folder: Save1/2/3.sgd, the supplied starting save'
    Write-Host '   - PopTracker packs\  (only if you give a path)'
    Write-Host ''
    Write-Host ' NEVER TOUCHES' -ForegroundColor White
    Write-Host '   - Save0.sgd. Your first save slot is left completely alone.'
    Write-Host '   - any other save, or any game file not listed above'
    Write-Host '   - the registry, services, scheduled tasks, or anything outside'
    Write-Host '     the folders listed above'
    Write-Host '   - the network, beyond the two BmSDK URLs above'
    Write-Host ''
    Write-Host ' Nothing is sent anywhere. There is no telemetry of any kind.'
    Write-Host ''
    Write-Host ' To undo: restore BatmanAC.exe.original_backup, delete the .cs files'
    Write-Host ' from BmGame\Scripts\, and delete the .apworld. Full steps are in'
    Write-Host ' docs/INSTALL.md under "Uninstalling".'
    Write-Host ''
    Write-Host '===============================================================' -ForegroundColor Cyan
}

# ---------------------------------------------------------------- detection

function Find-Game {
    if ($GamePath) {
        if (Test-Path (Join-Path $GamePath 'BmGame')) { return (Resolve-Path $GamePath).Path }
        throw "-GamePath was given but doesn't look like the game: $GamePath"
    }

    $roots = @()
    foreach ($steam in @("${env:ProgramFiles(x86)}\Steam", "$env:ProgramFiles\Steam")) {
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            $roots += $steam
            # every "path"  "X:\\SteamLibrary" entry
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $roots += $m.Groups[1].Value -replace '\\\\', '\'
            }
        }
    }
    foreach ($r in ($roots | Select-Object -Unique)) {
        $candidate = Join-Path $r 'steamapps\common\Batman Arkham City GOTY'
        if (Test-Path (Join-Path $candidate 'BmGame')) { return $candidate }
    }
    return $null
}

function Find-Archipelago {
    if ($ArchipelagoPath) { return $ArchipelagoPath }
    foreach ($p in @("$env:ProgramData\Archipelago",
                     "$env:LOCALAPPDATA\Archipelago",
                     "$env:USERPROFILE\Archipelago")) {
        if (Test-Path (Join-Path $p 'custom_worlds')) { return $p }
    }
    return $null
}

function Find-SaveFolder {
    # GetFolderPath respects OneDrive redirection, which trips people up.
    $docs = [Environment]::GetFolderPath('MyDocuments')
    $candidates = @(
        (Join-Path $docs 'WB Games\Batman Arkham City GOTY\SaveData'),
        (Join-Path $env:USERPROFILE 'Documents\WB Games\Batman Arkham City GOTY\SaveData'),
        (Join-Path $env:USERPROFILE 'OneDrive\Documents\WB Games\Batman Arkham City GOTY\SaveData')
    )
    foreach ($c in ($candidates | Select-Object -Unique)) {
        if (Test-Path $c) {
            $sub = Get-ChildItem $c -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($sub) { return $sub.FullName }
            return $c
        }
    }
    return $null
}

function Test-GameRunning {
    return [bool](Get-Process -Name 'BatmanAC' -ErrorAction SilentlyContinue)
}

# ---------------------------------------------------------------- actions

function Copy-Item-Reported {
    param($Source, $Dest, $Label)
    Act "$Label -> $Dest"
    if (-not $DryRun) {
        $dir = Split-Path -Parent $Dest
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        Copy-Item -Path $Source -Destination $Dest -Force
    }
}

function Install-BmSDK {
    param($Game)

    Head 'BmSDK'
    $sdkPresent = Test-Path (Join-Path $Game 'Binaries\Win32\sdk')
    if ($sdkPresent) { OK 'already installed - skipping download'; return }
    if ($SkipBmSDK)  { Warn 'not installed, but -SkipBmSDK was given'; return }
    if ($DryRun)     { Act 'would download and install BmSDK'; return }

    $tmp = Join-Path $env:TEMP "bmsdk_$(Get-Random)"
    New-Item -ItemType Directory -Path $tmp -Force | Out-Null
    try {
        Act 'querying the latest BmSDK release'
        $rel = Invoke-RestMethod -Uri $BmSdkLatestApi -Headers @{ 'User-Agent' = 'BatmanAC-AP-Installer' }
        $asset = $rel.assets | Where-Object { $_.name -like 'BmSDK-AC-*.zip' } | Select-Object -First 1
        if (-not $asset) { throw 'could not find a BmSDK zip on the latest release' }

        Act "downloading $($asset.name)  ($([math]::Floor($asset.size/1MB)) MB)"
        $zip = Join-Path $tmp $asset.name
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -UseBasicParsing

        Act 'extracting into the game folder'
        $ex = Join-Path $tmp 'sdk'
        Expand-Archive -Path $zip -DestinationPath $ex -Force
        Copy-Item -Path (Join-Path $ex '*') -Destination $Game -Recurse -Force
        OK "BmSDK $($rel.tag_name) installed"
    }
    finally {
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Install-CompatPatch {
    param($Game)

    Head 'Steam/GOG compatibility patch'
    $exe    = Join-Path $Game 'Binaries\Win32\BatmanAC.exe'
    $backup = "$exe.original_backup"

    if (Test-Path $backup) { OK 'already patched (backup exists) - skipping'; return }
    if ($DryRun) { Act 'would back up BatmanAC.exe and apply the patch'; return }

    Write-Host ''
    Write-Host '  This REPLACES Binaries\Win32\BatmanAC.exe with a patched build' -ForegroundColor Yellow
    Write-Host '  supplied by the BmSDK project. Steam and GOG copies need it or' -ForegroundColor Yellow
    Write-Host '  BmSDK will not load.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host "  Your current exe is copied to:" -ForegroundColor Yellow
    Write-Host "    $backup" -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  Steam may replace the patched exe again when it verifies files.' -ForegroundColor Yellow
    Write-Host '  If the mod stops loading, re-run this installer.' -ForegroundColor Yellow
    Write-Host ''
    if (-not $Yes) {
        $a = Read-Host '  Replace BatmanAC.exe? (yes/no)'
        if ($a -ne 'yes') { Warn 'skipped - BmSDK will not load until this is applied'; return }
    }

    $tmp = Join-Path $env:TEMP "bmcompat_$(Get-Random)"
    New-Item -ItemType Directory -Path $tmp -Force | Out-Null
    try {
        Act 'backing up your original executable'
        Copy-Item $exe $backup -Force

        Act 'downloading CompatibilityPatch.zip'
        $zip = Join-Path $tmp 'CompatibilityPatch.zip'
        Invoke-WebRequest -Uri $CompatPatchUrl -OutFile $zip -UseBasicParsing

        Act 'applying'
        $ex = Join-Path $tmp 'x'
        Expand-Archive -Path $zip -DestinationPath $ex -Force
        Copy-Item -Path (Join-Path $ex '*') -Destination $Game -Recurse -Force
        OK 'compatibility patch applied'
    }
    finally {
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Install-Scripts {
    param($Game)
    Head 'Game scripts'
    $dest = Join-Path $Game 'BmGame\Scripts'
    foreach ($f in $PlayerScripts) {
        $src = Join-Path $Here "game_scripts\$f"
        if (-not (Test-Path $src)) { Bad "missing from this download: $f"; continue }
        Copy-Item-Reported $src (Join-Path $dest $f) $f
    }
    OK "$($PlayerScripts.Count) scripts installed"
}

function Install-Apworld {
    param($Ap)
    Head 'Archipelago world'
    if (-not $Ap) { Warn 'Archipelago not found - copy releases\batman_arkham_city.apworld into custom_worlds\ yourself'; return }
    $src = Join-Path $Here 'releases\batman_arkham_city.apworld'
    Copy-Item-Reported $src (Join-Path $Ap 'custom_worlds\batman_arkham_city.apworld') 'batman_arkham_city.apworld'

    $yaml = Join-Path $Ap 'Players\BatmanArkhamCity.yaml'
    if (Test-Path $yaml) {
        OK 'YAML already present - left alone so your settings survive'
    } else {
        Copy-Item-Reported (Join-Path $Here 'yaml\BatmanArkhamCity.yaml') $yaml 'YAML template'
    }
}

function Install-Save {
    param($SaveDir)
    Head 'Starting save'
    if (-not $SaveDir) {
        Warn 'save folder not found - see docs/INSTALL.md step 4 to place it manually'
        return
    }
    Say "  folder: $SaveDir"
    $src = Join-Path $Here 'releases\CANONICAL_story_complete_no_riddler.sgd'
    foreach ($n in 1, 2, 3) {
        Copy-Item-Reported $src (Join-Path $SaveDir "Save$n.sgd") "Save$n.sgd"
    }
    OK 'slots 1-3 set to the starting save (Save0 untouched)'
}

function Install-Tracker {
    Head 'PopTracker pack'
    if (-not $PopTrackerPacksPath) {
        Warn 'no -PopTrackerPacksPath given - copy releases\*.zip into PopTracker''s packs\ folder yourself'
        return
    }
    $pack = Get-ChildItem (Join-Path $Here 'releases') -Filter 'batman_arkham_city_ap_*.zip' |
            Sort-Object Name -Descending | Select-Object -First 1
    if (-not $pack) { Bad 'no tracker pack found in releases\'; return }
    Copy-Item-Reported $pack.FullName (Join-Path $PopTrackerPacksPath $pack.Name) $pack.Name
}

# ---------------------------------------------------------------- check mode

function Invoke-Check {
    param($Game, $Ap, $SaveDir)
    Head 'Checking your install'

    if ($Game) { OK "game: $Game" } else { Bad 'game not found'; return }
    if (Test-Path (Join-Path $Game 'Binaries\Win32\sdk')) { OK 'BmSDK present' } else { Bad 'BmSDK NOT installed' }
    if (Test-Path (Join-Path $Game 'Binaries\Win32\BatmanAC.exe.original_backup')) {
        OK 'compatibility patch applied'
    } else { Warn 'compatibility patch not applied (no backup exe found)' }

    $missing = @()
    foreach ($f in $PlayerScripts) {
        if (-not (Test-Path (Join-Path $Game "BmGame\Scripts\$f"))) { $missing += $f }
    }
    if ($missing.Count) { Bad "scripts missing: $($missing -join ', ')" } else { OK 'all 6 game scripts present' }

    if ($Ap) {
        if (Test-Path (Join-Path $Ap 'custom_worlds\batman_arkham_city.apworld')) { OK 'apworld installed' }
        else { Bad 'apworld NOT in custom_worlds' }
    } else { Bad 'Archipelago not found' }

    if ($SaveDir) {
        $have = @(1,2,3 | Where-Object { Test-Path (Join-Path $SaveDir "Save$_.sgd") })
        if ($have.Count) { OK "save slots present: $($have -join ', ')  (in $SaveDir)" }
        else { Warn "no Save1/2/3 in $SaveDir" }
    } else { Bad 'save folder not found' }

    $logs = Join-Path $Game 'Binaries\Win32\ArchipelagoLogs'
    if (Test-Path $logs) { OK "logs: $logs" } else { Say "  (no logs yet - created on first run)" }
    Write-Host ''
    Say 'Paste this output into a bug report if something is wrong.'
}

# ---------------------------------------------------------------- main

if ($env:OS -ne 'Windows_NT') { Bad 'This installer is Windows only.'; exit 1 }

$game    = Find-Game
$ap      = Find-Archipelago
$saveDir = Find-SaveFolder

if ($Check) { Invoke-Check $game $ap $saveDir; exit 0 }

Show-Manifest

Head 'Detected'
if ($game)    { OK "game        : $game" }    else { Bad 'game        : NOT FOUND (use -GamePath)' }
if ($ap)      { OK "Archipelago : $ap" }      else { Warn 'Archipelago : not found (use -ArchipelagoPath)' }
if ($saveDir) { OK "saves       : $saveDir" } else { Warn 'saves       : not found (place the save manually)' }

if (-not $game) {
    Write-Host ''
    Bad 'Cannot continue without the game folder.'
    Say 'Re-run with:  .\install.ps1 -GamePath "D:\SteamLibrary\steamapps\common\Batman Arkham City GOTY"'
    exit 1
}

if (Test-GameRunning) {
    Write-Host ''
    Bad 'Batman: Arkham City is running. Close it completely and re-run.'
    exit 1
}

if ($DryRun) { Head 'DRY RUN - nothing will be modified' }
elseif (-not $Yes) {
    Write-Host ''
    $answer = Read-Host 'Type YES to proceed (anything else cancels)'
    if ($answer -ne 'YES') { Say 'Cancelled. Nothing was changed.'; exit 0 }
}

if (-not $SkipBmSDK) {
    Install-BmSDK      $game
    Install-CompatPatch $game
} else {
    Head 'BmSDK'; Warn 'skipped at your request'
}
Install-Scripts $game
Install-Apworld $ap
Install-Save    $saveDir
Install-Tracker

Head 'Done'
if ($DryRun) {
    Say 'Dry run only - nothing was changed.'
} else {
    Say 'Next steps:'
    Say '  1. Edit the YAML in Archipelago''s Players folder (set your slot name)'
    Say '  2. Generate and host a seed'
    Say '  3. Start the game and load Save 1, 2 or 3'
    Say '  4. ArchipelagoLauncher -> "Batman: Arkham City Client" -> connect'
    Write-Host ''
    Say 'Verify any time with:  .\install.ps1 -Check'
    Say 'Trouble? docs\TROUBLESHOOTING.md'
}
Write-Host ''
