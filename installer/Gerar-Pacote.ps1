# Builds the lab installation package (see installer/pacote/LEIA-ME.txt):
#
#   powershell -ExecutionPolicy Bypass -File installer\Gerar-Pacote.ps1 [-Output <folder>]
#
# Needs the .NET 8 SDK. The programs are published self-contained for
# win-x64, so neither the lab machines nor the teacher's PC need .NET.
#
# Rebuilding into an existing package replaces only the programs and
# scripts: trusted-masters.json (the teachers authorized in it) and
# maquinas\ (the machines already installed from it) are kept.
#
# This file stays ASCII-only: Windows PowerShell 5.1 reads a .ps1 without a
# BOM as ANSI.
[CmdletBinding()]
param(
    [string]$Output
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Path $PSScriptRoot -Parent
if (-not $Output) {
    $Output = Join-Path $repository 'dist\Midview-Instalacao'
}
$programs = Join-Path $Output 'programa'

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
function Write-BuildManifest([string]$Folder) {
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
    $label = (Get-Date).ToString('yyyy-MM-dd HH:mm', [Globalization.CultureInfo]::InvariantCulture) + " $commit"

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

foreach ($folder in 'aluno', 'professor', 'scripts') {
    $path = Join-Path $programs $folder
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

# Agent and Session side by side: the Agent launches the Session from its
# own folder. The Session goes second on purpose: the few runtime files both
# ship (WindowsBase, System.Drawing, EventLog...) must be the Windows Desktop
# runtime's full, same-or-newer versions the Session needs; the Agent runs
# fine on those, while the Session would break on the Agent's stubs.
Publish-Project 'Midview.Agent' (Join-Path $programs 'aluno')
Publish-Project 'Midview.Session' (Join-Path $programs 'aluno')
Write-BuildManifest (Join-Path $programs 'aluno')
Publish-Project 'Midview.Master' (Join-Path $programs 'professor')

$scripts = New-Item -ItemType Directory -Path (Join-Path $programs 'scripts') -Force
foreach ($script in Get-ChildItem -Path (Join-Path $PSScriptRoot 'scripts') -Filter '*.ps1') {
    Copy-Text $script.FullName (Join-Path $scripts.FullName $script.Name) $true
}
foreach ($file in Get-ChildItem -Path (Join-Path $PSScriptRoot 'pacote') -File) {
    # The .cmd files are ASCII-only; a BOM would break their first line.
    Copy-Text $file.FullName (Join-Path $Output $file.Name) ($file.Extension -ne '.cmd')
}
New-Item -ItemType Directory -Path (Join-Path $Output 'maquinas') -Force | Out-Null

Write-Host ''
Write-Host "Package ready: $Output" -ForegroundColor Green
Write-Host 'Copy the whole folder to a USB stick and follow LEIA-ME.txt.'
