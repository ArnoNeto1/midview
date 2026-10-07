# Checks a built setup's scripts and contents under whatever PowerShell runs
# this -- in CI (.github/workflows/pacote.yml), Windows PowerShell 5.1, the
# one lab machines have:
#
#   powershell -ExecutionPolicy Bypass -File installer\tests\Testar-Scripts.ps1 -Pacote dist
#
# -Pacote is installer\Gerar-Instalador.ps1's output: the setup, and in
# programa\ what went into it. The scripts are checked there because those
# copies are the ones with the BOM 5.1 needs (see Gerar-Instalador.ps1). The
# shared functions (Comum.ps1) are run for real.
#
# ASCII-only: 5.1 reads a .ps1 without a BOM as ANSI.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Pacote
)

$ErrorActionPreference = 'Stop'
$script:failures = 0

function Check([bool]$Condition, [string]$What) {
    if ($Condition) {
        Write-Host "ok    $What"
    }
    else {
        Write-Host "FALHA $What" -ForegroundColor Red
        $script:failures++
    }
}

Write-Host "PowerShell $($PSVersionTable.PSVersion) ($($PSVersionTable.PSEdition))"
$scripts = Join-Path $Pacote 'programa\instalador'

foreach ($file in Get-ChildItem -Path $scripts -Filter '*.ps1') {
    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors) | Out-Null
    Check ($errors.Count -eq 0) "$($file.Name) is valid PowerShell $(($errors | ForEach-Object { $_.Message }) -join '; ')"
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    Check ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) "$($file.Name) starts with a UTF-8 BOM"
}

foreach ($name in 'Comum.ps1', 'Parar-Aluno.ps1', 'Configurar-Aluno.ps1', 'Desinstalar-Aluno.ps1') {
    Check (Test-Path -LiteralPath (Join-Path $scripts $name)) "the setup carries $name"
}
Check (-not (Test-Path -LiteralPath (Join-Path $scripts 'Instalar-Aluno.ps1'))) 'and no longer the USB-stick installer'

[xml]$buildProps = Get-Content -LiteralPath (Join-Path (Split-Path -Path (Split-Path -Path $PSScriptRoot -Parent) -Parent) 'Directory.Build.props') -Raw
$midviewVersion = "$($buildProps.Project.PropertyGroup.Version)".Trim()
$setup = Join-Path $Pacote 'MidviewSetup.exe'
Check (Test-Path -LiteralPath $setup) 'the setup is MidviewSetup.exe'
$programsThere = @(Get-ChildItem -LiteralPath $Pacote -Filter '*.exe')
Check ($programsThere.Count -eq 1) "and it is the only program there ($(($programsThere | ForEach-Object { $_.Name }) -join ', '))"
if (Test-Path -LiteralPath $setup) {
    # Midview's version (Directory.Build.props), not in the name: in the
    # properties of the setup and of each program. Inno pads the text with
    # spaces Windows doesn't show.
    $setupVersion = "$((Get-Item -LiteralPath $setup).VersionInfo.ProductVersion)".Trim()
    Check ($setupVersion -eq $midviewVersion) "its version is Midview's, $midviewVersion, in its file properties ($setupVersion)"
}

foreach ($program in 'aluno\Midview.Agent.exe', 'aluno\Midview.Session.exe', 'professor\Midview.Master.exe') {
    $path = Join-Path $Pacote "programa\$program"
    Check (Test-Path -LiteralPath $path) "the setup has $program"
    if (Test-Path -LiteralPath $path) {
        $programVersion = "$((Get-Item -LiteralPath $path).VersionInfo.ProductVersion)".Trim()
        Check ($programVersion -eq $midviewVersion) "and it is version $midviewVersion ($programVersion)"
    }
}

# What the MidviewMaster reads to push the student program over the network
# (BuildManifest): exactly the files in the folder, besides itself.
$student = Join-Path $Pacote 'programa\aluno'
$manifestPath = Join-Path $student 'midview-build.json'
Check (Test-Path -LiteralPath $manifestPath) 'the student program has midview-build.json'
if (Test-Path -LiteralPath $manifestPath) {
    $bytes = [IO.File]::ReadAllBytes($manifestPath)
    Check (-not ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB)) 'midview-build.json has no BOM'
    $manifest = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
    Check ($manifest.build -match '^\d{4}-\d{2}-\d{2} \d{2}:\d{2} \S+$') "the build is labelled with its date, time and commit ($($manifest.build))"
    Check ($manifest.updateProtocol -eq 1) 'the manifest speaks update protocol 1'
    $listed = @($manifest.files)
    Check ($listed -contains 'Midview.Agent.exe' -and $listed -contains 'Midview.Session.exe') 'the manifest lists the Agent and the Session'
    Check (-not ($listed -contains 'midview-build.json')) 'the manifest does not list itself'
    Check (-not ($listed | Where-Object { $_ -like '*\*' })) "the manifest's paths use '/'"
    $root = (Get-Item -LiteralPath $student).FullName.TrimEnd('\') + '\'
    $actual = @(Get-ChildItem -LiteralPath $student -Recurse -File -Force |
            ForEach-Object { $_.FullName.Substring($root.Length).Replace('\', '/') } |
            Where-Object { $_ -ne 'midview-build.json' })
    $missing = @($listed | Where-Object { $actual -notcontains $_ })
    $unlisted = @($actual | Where-Object { $listed -notcontains $_ })
    Check ($missing.Count -eq 0 -and $unlisted.Count -eq 0) "the manifest lists exactly the $($actual.Count) files in the folder (missing: $($missing -join ', '); unlisted: $($unlisted -join ', '))"
}

. (Join-Path $scripts 'Comum.ps1')

$work = Join-Path ([IO.Path]::GetTempPath()) ('midview-scripts-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $work | Out-Null
try {
    # What the setup shows the teacher when a script fails: UTF-8 without a
    # BOM, accents intact ("nao" with its accent, spelled out to keep this
    # file ASCII).
    $nao = "n$([char]0x00E3)o"
    $erro = Join-Path $work 'erro.txt'
    Write-Erro $erro "$nao parou"
    $bytes = [IO.File]::ReadAllBytes($erro)
    Check (-not ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB)) 'an error message is written without a BOM'
    Check ([IO.File]::ReadAllText($erro, [Text.Encoding]::UTF8) -eq "$nao parou") 'and reads back with its accents'
    Write-Erro '' 'x'
    Check $true 'no error file asked for, nothing written'

    # The service's PathName, as Win32_Service reports it.
    $exe = 'C:\Program Files\Midview.update\AB12\Midview.Agent.exe'
    Check ((ConvertFrom-CaminhoDoServico "`"$exe`"") -eq $exe) "a quoted service path is read"
    Check ((ConvertFrom-CaminhoDoServico "`"$exe`" --flag") -eq $exe) "a quoted service path with arguments is read"
    Check ((ConvertFrom-CaminhoDoServico 'C:\Midview\Midview.Agent.exe') -eq 'C:\Midview\Midview.Agent.exe') "an unquoted service path is read"
    Check ($null -eq (ConvertFrom-CaminhoDoServico '') -and $null -eq (ConvertFrom-CaminhoDoServico '"')) "an empty service path is nothing"

    # The lock a network update holds (the Agent's UpdateLock).
    $lock = Join-Path $work 'update.lock'
    Check ($null -eq (Enter-TravaDeAtualizacao (Join-Path $work 'nowhere\update.lock') 1)) 'no data folder, no lock to wait for'
    $held = [IO.File]::Open($lock, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        Enter-TravaDeAtualizacao $lock 2 | Out-Null
        Check $false 'a held update lock stops the install after the wait'
    }
    catch {
        Check ($_.Exception.Message -like '*atualiza*') 'a held update lock stops the install after the wait'
    }
    finally {
        $held.Dispose()
    }
    $mine = Enter-TravaDeAtualizacao $lock 2
    try {
        Check ($null -ne $mine) 'a free update lock is taken'
        $again = $null
        try {
            $again = [IO.File]::Open($lock, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        }
        catch {
        }
        Check ($null -eq $again) 'and held until released'
        if ($again) {
            $again.Dispose()
        }
    }
    finally {
        if ($mine) {
            $mine.Dispose()
        }
    }

    # What a network update leaves behind (Remove-RestosDeAtualizacao), in
    # stand-in folders, with a file held open so Midview.update can't go.
    $PastaAtualizacoes = Join-Path $work 'Midview.update'
    $PastaEstadoAtualizacao = Join-Path $work 'estado'
    $TarefaRecuperacao = 'Midview - tarefa de teste que nao existe'
    New-Item -ItemType Directory -Path (Join-Path $PastaAtualizacoes 'AB12') -Force | Out-Null
    New-Item -ItemType Directory -Path $PastaEstadoAtualizacao -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $PastaEstadoAtualizacao 'update-state.json'), '{}')
    $held = [IO.File]::Open((Join-Path $PastaAtualizacoes 'AB12\Midview.Agent.dll'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        Remove-RestosDeAtualizacao 3>$null
        Check $true 'a Midview.update that cannot be deleted only warns after an install'
        Check (-not (Test-Path -LiteralPath (Join-Path $PastaEstadoAtualizacao 'update-state.json'))) 'the update record is deleted'
        try {
            Remove-RestosDeAtualizacao -Obrigatorio 3>$null
            Check $false 'but it stops an uninstall'
        }
        catch {
            Check ($_.Exception.Message -like "*$PastaAtualizacoes*") 'but it stops an uninstall'
        }
    }
    finally {
        $held.Dispose()
    }
    Remove-RestosDeAtualizacao -Obrigatorio
    Check (-not (Test-Path -LiteralPath $PastaAtualizacoes)) 'and goes once nothing holds it'

    Check ((Split-Path -Leaf (Join-Path $env:ProgramFiles 'Midview')) -eq 'Midview') 'the default install folder is named Midview'
    try {
        Assert-PastaDoMidview 'C:\'
        Check $false 'a folder not named Midview is refused'
    }
    catch {
        Check $true 'a folder not named Midview is refused'
    }

    # Each script run the way the setup runs it: powershell -File with only
    # -Erro. Windows PowerShell 5.1 leaves $PSScriptRoot empty in param()
    # defaults, which once stopped both before their first line with no
    # message for the setup to show. From a stand-in folder not named
    # Midview, so they stop at that check (or, not elevated, at the
    # administrator one) and touch nothing.
    $standIn = Join-Path $work 'outra-pasta\instalador'
    New-Item -ItemType Directory -Path $standIn -Force | Out-Null
    foreach ($name in 'Comum.ps1', 'Configurar-Aluno.ps1', 'Desinstalar-Aluno.ps1') {
        Copy-Item -LiteralPath (Join-Path $scripts $name) -Destination $standIn
    }
    $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    foreach ($name in 'Configurar-Aluno.ps1', 'Desinstalar-Aluno.ps1') {
        $erroScript = Join-Path $work "erro-$name.txt"
        $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$(Join-Path $standIn $name)`"", '-Erro', "`"$erroScript`"")
        $process = Start-Process -FilePath $powershell -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
        $message = if (Test-Path -LiteralPath $erroScript) { [IO.File]::ReadAllText($erroScript, [Text.Encoding]::UTF8) } else { '' }
        Check ($process.ExitCode -eq 1 -and ($message -like '*Midview*' -or $message -like '*administrador*')) "$name run as the setup runs it gets to its checks and says why it stopped (exit $($process.ExitCode): $message)"
    }
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force
}

if ($script:failures -gt 0) {
    Write-Host "$($script:failures) check(s) failed." -ForegroundColor Red
    exit 1
}
Write-Host 'All checks passed.' -ForegroundColor Green
