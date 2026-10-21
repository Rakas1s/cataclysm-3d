# Sparse-clones a pinned CDDA release (plus any external tilesets in config/tilesets.json)
# and copies their content into cdda-snapshot/.
param(
    [string]$Tag = "0.I-1",
    [string]$Repo = "https://github.com/CleverRaven/Cataclysm-DDA.git"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root "cdda-snapshot"
$tmp = [System.IO.Path]::GetTempPath()
$snapshot = @()

function Get-Sparse([string]$url, [string]$ref, [string[]]$paths, [string]$work) {
    # git writes progress to stderr, which Windows PowerShell turns into errors under "Stop".
    $ErrorActionPreference = "Continue"
    if (Test-Path $work) { Remove-Item -Recurse -Force $work }
    git clone --depth 1 --filter=blob:none --sparse --branch $ref $url $work
    if ($LASTEXITCODE -ne 0) { throw "git clone $url failed" }
    git -C $work sparse-checkout set --no-cone @($paths | ForEach-Object { "/$_" })
    if ($LASTEXITCODE -ne 0) { throw "sparse-checkout in $url failed" }
    return (git -C $work rev-parse HEAD)
}

function Copy-Into([string]$src, [string]$target) {
    if (-not (Test-Path -LiteralPath $src)) { Write-Warning "Missing: $src"; return }
    if (Test-Path -LiteralPath $target) { Remove-Item -Recurse -Force -LiteralPath $target }
    New-Item -ItemType Directory -Force (Split-Path -Parent $target) | Out-Null
    Copy-Item -Recurse -LiteralPath $src $target
}

$tilesets = (Get-Content (Join-Path $root "config/tilesets.json") -Raw | ConvertFrom-Json).tilesets

# CDDA itself: data plus tilesets that live in the CDDA repo.
$cddaPaths = @("data/json/", "data/core/", "LICENSE.txt")
$cddaTilesets = @($tilesets | Where-Object { -not $_.repo })
$cddaPaths += @($cddaTilesets | ForEach-Object { "$($_.repoPath)/" })

$work = Join-Path $tmp "cdda-snapshot-$Tag"
$sha = Get-Sparse $Repo $Tag $cddaPaths $work
foreach ($p in @("data/json", "data/core", "LICENSE.txt")) { Copy-Into (Join-Path $work $p) (Join-Path $dest $p) }
foreach ($t in $cddaTilesets) { Copy-Into (Join-Path $work $t.repoPath) (Join-Path $root $t.path) }
Remove-Item -Recurse -Force $work
$snapshot += "cdda.tag=$Tag", "cdda.commit=$sha", "cdda.repo=$Repo"

# External tilesets.
foreach ($t in @($tilesets | Where-Object { $_.repo })) {
    $work = Join-Path $tmp ("tileset-" + [guid]::NewGuid().ToString("N"))
    $tsha = Get-Sparse $t.repo $t.branch @("$($t.repoPath)/", "LICENSE.txt", "credits.txt") $work
    Copy-Into (Join-Path $work $t.repoPath) (Join-Path $root $t.path)
    foreach ($f in @("LICENSE.txt", "credits.txt")) {
        if (Test-Path (Join-Path $work $f)) { Copy-Item (Join-Path $work $f) (Join-Path $root $t.path) -Force }
    }
    Remove-Item -Recurse -Force $work
    $snapshot += "tileset[$($t.name)].repo=$($t.repo)", "tileset[$($t.name)].commit=$tsha"
}

$snapshot += "fetched=$(Get-Date -Format o)"
$snapshot | Set-Content -Encoding utf8 (Join-Path $dest "SNAPSHOT.txt")
Write-Host "Snapshot written to $dest"
