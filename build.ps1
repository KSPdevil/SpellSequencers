# SpellSequencers - build + package script
# Double click "BUILD.bat" instead of running this file directly.
# Compiles Main.cs (with embedded .jbp blueprints) into SpellSequencers.dll and packs an installable zip.

param(
    [switch]$Debug,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'

function Say($msg, $color = 'Cyan') { Write-Host "[SEQ] $msg" -ForegroundColor $color }

$modDir = $PSScriptRoot
if (-not $modDir) { $modDir = Split-Path -Parent $MyInvocation.MyCommand.Path }

$gameDir  = 'F:\yongwangzheZWYZTK V2.1.1\Pathfinder Wrath of the Righteous'
$managed  = Join-Path $gameDir 'Wrath_Data\Managed'
$src      = Join-Path $modDir 'Main.cs'
$dll      = Join-Path $modDir 'SpellSequencers.dll'
$info     = Join-Path $modDir 'Info.json'
$bpDir    = Join-Path $modDir 'Blueprints'
$zipOut   = Join-Path $modDir 'SpellSequencers-UMM.zip'

Say 'SpellSequencers build script'
Write-Host ''

# ---------------------------------------------------------------- checks
if (-not (Test-Path $managed)) { Say "Managed not found: $managed" 'Red'; if (-not $NoPause) { Read-Host 'Press Enter' }; exit 1 }
if (-not (Test-Path $src))     { Say "Source not found: $src" 'Red';      if (-not $NoPause) { Read-Host 'Press Enter' }; exit 1 }
if (-not (Test-Path $info))    { Say "Info.json not found: $info" 'Red';  if (-not $NoPause) { Read-Host 'Press Enter' }; exit 1 }

# ---------------------------------------------------------------- locate compiler
Say 'Locating a C# compiler...'
$compiler = $null   # @{ Exe ; Prefix }  ->  & Exe @Prefix @rsp

# 1) 仓库自带的 Roslyn（上级 mmmods\.tools 目录，离线可用）
$roslynLocal = Join-Path (Split-Path -Parent $modDir) '.tools\roslyn\bincore\csc.dll'
$dotnet      = Get-Command dotnet -ErrorAction SilentlyContinue

if ((Test-Path $roslynLocal) -and $dotnet) {
    $compiler = @{ Exe = $dotnet.Source; Prefix = @($roslynLocal) }
    Say "Using Roslyn (bundled): $roslynLocal"
}

if (-not $compiler) {
    $fw = @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($fw) {
        $compiler = @{ Exe = $fw; Prefix = @() }
        Say "Using .NET Framework compiler: $fw"
    }
}

if (-not $compiler) {
    Say 'ERROR: no C# compiler available.' 'Red'
    if (-not $NoPause) { Read-Host 'Press Enter' }
    exit 1
}

# ---------------------------------------------------------------- build response file
$exclude = @('netstandard.dll', 'UnityEngine.dll')
$refs = Get-ChildItem -Path $managed -Filter '*.dll' |
        Where-Object { $exclude -notcontains $_.Name -and $_.Name -notlike 'System.Private.*' }

$rsp = Join-Path $env:TEMP 'seq-build.rsp'
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('/noconfig')
$lines.Add('/nostdlib+')
$lines.Add('/target:library')
$lines.Add('/langversion:latest')
$lines.Add('/optimize-')
$lines.Add('/warn:0')
if ($Debug) { $lines.Add('/define:SEQ_DEBUG') }
$lines.Add("/out:`"$dll`"")
foreach ($r in $refs) { $lines.Add("/r:`"$($r.FullName)`"") }
$lines.Add("`"$src`"")

# 嵌入 .jbp 蓝图为资源（逻辑名保持文件名，运行时按 .jbp 后缀枚举）
Get-ChildItem -Path $bpDir -Filter '*.jbp' -ErrorAction SilentlyContinue | ForEach-Object {
    $lines.Add('/resource:"' + $_.FullName + '",' + $_.Name)
}

[System.IO.File]::WriteAllLines($rsp, $lines, [System.Text.Encoding]::ASCII)

Say "References: $($refs.Count) assemblies from Managed"
if ($Debug) { Say 'DEBUG build: runtime logging enabled' 'Yellow' }

# ---------------------------------------------------------------- compile
if (Test-Path $dll) { Remove-Item $dll -Force }
Say 'Compiling...'

if ($compiler.Prefix.Count -gt 0) { $env:DOTNET_ROLL_FORWARD = 'LatestMajor' }

$out = & $compiler.Exe @($compiler.Prefix) "@$rsp" 2>&1
$code = $LASTEXITCODE
$out | ForEach-Object { Write-Host $_ }

if ($code -ne 0 -or -not (Test-Path $dll)) {
    Say "COMPILE FAILED (exit $code). Errors above - copy them back to me." 'Red'
    if (-not $NoPause) { Read-Host 'Press Enter' }
    exit $code
}

$size = (Get-Item $dll).Length
Say "Compiled OK: $dll ($size bytes)" 'Green'

# ---------------------------------------------------------------- package zip
try {
    if (Test-Path $zipOut) { Remove-Item $zipOut -Force }
    Compress-Archive -Path @($dll, $info) -DestinationPath $zipOut -CompressionLevel Optimal
    Say "ZIP created: $zipOut" 'Green'
    Say 'Install it with Unity Mod Manager: Mods -> Install -> pick this zip.' 'Green'
} catch {
    Say "ZIP failed: $($_.Exception.Message)" 'Yellow'
}

# ---------------------------------------------------------------- optional direct install
$target = Join-Path $gameDir 'Mods\SpellSequencers'
try {
    if (Test-Path (Split-Path -Parent (Join-Path $gameDir 'Mods'))) {
        New-Item -ItemType Directory -Force -Path $target | Out-Null
        Copy-Item $dll  -Destination $target -Force
        Copy-Item $info -Destination $target -Force
        Say "Also copied to: $target" 'Green'
    }
} catch {
    Say "Direct copy skipped: $($_.Exception.Message)" 'Yellow'
}

Write-Host ''
Say 'ALL DONE.' 'Green'
if (-not $NoPause) { Read-Host 'Press Enter to close' }
