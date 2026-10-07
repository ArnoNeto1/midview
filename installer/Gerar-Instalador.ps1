# Builds Midview's setup program (docs/superpowers/specs/2026-10-05-installer-and-pairing-design.md):
#
#   powershell -ExecutionPolicy Bypass -File installer\Gerar-Instalador.ps1 [-Output <folder>] [-Iscc <ISCC.exe>]
#
# Needs the .NET 8 SDK and Inno Setup 6. The programs are published
# self-contained for win-x64, so no computer needs .NET installed. Output:
#   <Output>\MidviewSetup.exe   the setup; Midview's version (Directory.Build.props) is in its properties
#   <Output>\programa\aluno, professor, instalador   what went into it (the tests use them)
#
# This file stays ASCII-only: Windows PowerShell 5.1 reads a .ps1 without a
# BOM as ANSI.
[CmdletBinding()]
param(
    [string]$Output,
    [string]$Iscc
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Path $PSScriptRoot -Parent
if (-not $Output) {
    $Output = Join-Path $repository 'dist'
}
$programs = Join-Path $Output 'programa'

function Find-Iscc {
    if ($Iscc) {
        return $Iscc
    }
    foreach ($candidate in @(
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'))) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }
    $command = Get-Command 'iscc.exe' -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }
    throw 'Inno Setup 6 not found: install it from https://jrsoftware.org/isdl.php or pass -Iscc <ISCC.exe>.'
}

function Publish-Project([string]$Project, [string]$Destination) {
    Write-Host "Publishing $Project -> $Destination" -ForegroundColor Cyan
    & dotnet publish (Join-Path $repository "src\$Project") -c Release -r win-x64 --self-contained true `
        -o $Destination -p:DebugType=None -p:DebugSymbols=false --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish $Project failed (exit code $LASTEXITCODE)."
    }
}

# Normalizes line endings to CRLF (cmd.exe misreads labels in LF-only batch
# files) and writes UTF-8, with a BOM where asked (Windows PowerShell 5.1
# garbles the scripts' Portuguese text without one).
function Copy-Text([string]$Source, [string]$Destination, [bool]$WithBom) {
    $text = [IO.File]::ReadAllText($Source) -replace "`r?`n", "`r`n"
    [IO.File]::WriteAllText($Destination, $text, (New-Object Text.UTF8Encoding $WithBom))
}

# midview-build.json, next to the student program: what the MidviewMaster
# needs to push it over the network (BuildManifest in Midview.Protocol) --
# a label (build time and git commit), the update protocol, and exactly the
# files published, as '/'-separated paths relative to the folder. Written by
# hand, without BOM: 5.1's ConvertTo-Json changes one-item lists.
function Write-BuildManifest([string]$Folder, [datetime]$When) {
    $commit = 'local'
    try {
        $hash = & git -C $repository rev-parse --short HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and $hash) {
            $commit = "$hash".Trim()
        }
    }
    catch {
        # No git: the label just says "local".
    }
    $label = $When.ToString('yyyy-MM-dd HH:mm', [Globalization.CultureInfo]::InvariantCulture) + " $commit"

    $separator = [IO.Path]::DirectorySeparatorChar
    $root = (Get-Item -LiteralPath $Folder).FullName.TrimEnd($separator) + $separator
    $files = New-Object System.Collections.Generic.List[string]
    foreach ($file in Get-ChildItem -LiteralPath $Folder -Recurse -File -Force) {
        $relative = $file.FullName.Substring($root.Length).Replace([string]$separator, '/')
        if ($relative -ne 'midview-build.json') {
            $files.Add($relative)
        }
    }
    $files.Sort([StringComparer]::Ordinal)

    $quote = { param($text) '"' + ($text -replace '\\', '\\' -replace '"', '\"') + '"' }
    $items = foreach ($relative in $files) { '    ' + (& $quote $relative) }
    $json = @(
        '{'
        '  "build": ' + (& $quote $label) + ','
        '  "updateProtocol": 1,'
        '  "files": ['
        ($items -join ",`r`n")
        '  ]'
        '}'
    ) -join "`r`n"
    [IO.File]::WriteAllText((Join-Path $Folder 'midview-build.json'), $json, (New-Object Text.UTF8Encoding $false))
    Write-Host "Build $label ($($files.Count) files)" -ForegroundColor Cyan
}

$iscc = Find-Iscc
$when = Get-Date
$invariant = [Globalization.CultureInfo]::InvariantCulture
# Midview's version, set in Directory.Build.props (the programs get it from
# there too). The build's date, time and commit go in midview-build.json.
[xml]$buildProps = Get-Content -LiteralPath (Join-Path $repository 'Directory.Build.props') -Raw
$version = "$($buildProps.Project.PropertyGroup.Version)".Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Directory.Build.props needs a Version like 0.0.1 (found '$version')."
}

New-Item -ItemType Directory -Path $Output -Force | Out-Null
if (Test-Path -LiteralPath $programs) {
    Remove-Item -LiteralPath $programs -Recurse -Force
}
# The setup from an earlier run, under its name now or the one it had.
foreach ($earlier in 'MidviewSetup.exe', 'Midview-Instalador-*.exe') {
    Get-ChildItem -LiteralPath $Output -Filter $earlier | Remove-Item -Force
}

# Agent and Session side by side: the Agent launches the Session from its
# own folder. The Session goes second on purpose: the few runtime files both
# ship (WindowsBase, System.Drawing, EventLog...) must be the Windows Desktop
# runtime's full, same-or-newer versions the Session needs; the Agent runs
# fine on those, while the Session would break on the Agent's stubs.
Publish-Project 'Midview.Agent' (Join-Path $programs 'aluno')
Publish-Project 'Midview.Session' (Join-Path $programs 'aluno')
Write-BuildManifest (Join-Path $programs 'aluno') $when
Publish-Project 'Midview.Master' (Join-Path $programs 'professor')

$scripts = New-Item -ItemType Directory -Path (Join-Path $programs 'instalador') -Force
foreach ($script in Get-ChildItem -Path (Join-Path $PSScriptRoot 'scripts') -Filter '*.ps1') {
    Copy-Text $script.FullName (Join-Path $scripts.FullName $script.Name) $true
}

# Compiled from a UTF-8-with-BOM copy, so Inno reads the Portuguese right.
$iss = Join-Path $programs 'Midview.iss'
Copy-Text (Join-Path $PSScriptRoot 'Midview.iss') $iss $true
$assets = Join-Path $repository 'assets'
& $iscc '/Q' "/DVersao=$version" "/DPrograma=$programs" "/DSaida=$Output" "/DAssets=$assets" $iss
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed (exit code $LASTEXITCODE)."
}

$setup = Join-Path $Output 'MidviewSetup.exe'
Write-Host ''
Write-Host "Setup ready: $setup (version $version)" -ForegroundColor Green
